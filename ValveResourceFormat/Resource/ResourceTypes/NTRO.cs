using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a resource with introspection data.
    /// </summary>
    public class NTRO : Block
    {
        private const byte PolymorphicStructFlag = 1;

        private enum SpecialFieldBehavior
        {
            None,
            BinaryBlob,
            Rows,
            Discard,
        }

        private readonly record struct SpecialField(SpecialFieldBehavior Behavior, int Columns = 0);

        // Fields that are not converted as plain values: raw data kept as a blob (byte arrays always are),
        // fixed size SIMD lane arrays split into rows, and data that is dropped
        private static readonly Dictionary<(string Struct, string Field), SpecialField> SpecialFields = new()
        {
            [("FeEdgeDesc_t", "nSide")] = new(SpecialFieldBehavior.Rows, 2),
            [("FeSimdTri_t", "nNode")] = new(SpecialFieldBehavior.Rows, 4),
            [("FeSimdQuad_t", "nNode")] = new(SpecialFieldBehavior.Rows, 4),
            [("FeSimdRodConstraint_t", "nNode")] = new(SpecialFieldBehavior.Rows, 4),
            [("FeSimdAnimStrayRadius_t", "nNode")] = new(SpecialFieldBehavior.Rows, 4),
            [("FeSimdSpringIntegrator_t", "nNode")] = new(SpecialFieldBehavior.Rows, 4),
            [("AnimationResourceData_t", "m_frameData")] = new(SpecialFieldBehavior.Discard),
            [("PostProcessingResource_t", "m_colorCorrectionVolumeData")] = new(SpecialFieldBehavior.BinaryBlob),
        };

        /// <summary>
        /// Gets the output data.
        /// </summary>
        public KVObject Output { get; private set; } = null!;
        /// <summary>
        /// Gets the struct name.
        /// </summary>
        public string? StructName { get; init; }

        private BinaryReader Reader => Resource.Reader!;
        private ResourceIntrospectionManifest? IntrospectionManifest;

        /// <inheritdoc/>
        public override BlockType Type => BlockType.DATA;

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            IntrospectionManifest = (ResourceIntrospectionManifest?)Resource.GetBlockByType(BlockType.NTRO)
                ?? throw new InvalidOperationException("Resource does not contain an NTRO block.");

            try
            {
                if (StructName != null)
                {
                    var refStruct = IntrospectionManifest.ReferencedStructs.Find(s => s.Name == StructName)
                        ?? throw new InvalidOperationException($"Could not find struct '{StructName}' in introspection manifest.");

                    Output = ReadStructure(refStruct, Offset);

                    return;
                }

                foreach (var refStruct in IntrospectionManifest.ReferencedStructs)
                {
                    Output = ReadStructure(refStruct, Offset);

                    break;
                }
            }
            finally
            {
                IntrospectionManifest = null;
            }
        }

        private KVObject ReadStructure(ResourceIntrospectionManifest.ResourceDiskStruct refStruct, long startingOffset)
        {
            Debug.Assert(IntrospectionManifest != null);

            var structEntry = KVObject.Collection();

            // Polymorphic structs store the id of their concrete type at the start of the object
            var concreteStruct = refStruct;

            if ((refStruct.StructFlags & PolymorphicStructFlag) != 0)
            {
                Reader.BaseStream.Position = startingOffset;
                concreteStruct = IntrospectionManifest.GetStructById(Reader.ReadUInt32()) ?? refStruct;

                // Same key KV3 uses to name the type of polymorphic objects
                structEntry.Add("_class", concreteStruct.Name);
            }

            ReadStructureFields(concreteStruct, startingOffset, structEntry);

            // The base struct type is not written out, so ReadStructure cannot be used for it.
            // Ancestor fields use offsets from the same object, so keep appending up the chain.
            // The depth limit guards against cycles.
            var baseStructId = concreteStruct.BaseStructId;

            for (var depth = 0; baseStructId != 0 && depth < 16; depth++)
            {
                var baseStruct = IntrospectionManifest.GetStructById(baseStructId);

                if (baseStruct == null)
                {
                    break;
                }

                ReadStructureFields(baseStruct, startingOffset, structEntry);

                baseStructId = baseStruct.BaseStructId;
            }

            // Some structs are padded, so all the field sizes do not add up to the size on disk
            Reader.BaseStream.Position = startingOffset + refStruct.DiskSize;

            return structEntry;
        }

        private void ReadStructureFields(ResourceIntrospectionManifest.ResourceDiskStruct refStruct, long startingOffset, KVObject structEntry)
        {
            foreach (var field in refStruct.FieldIntrospection)
            {
                // A negative offset (-1) means this field is not serialized to disk, there is nothing to read
                if (field.OnDiskOffset < 0)
                {
                    continue;
                }

                Reader.BaseStream.Position = startingOffset + field.OnDiskOffset;

                ReadFieldIntrospection(refStruct, field, structEntry);
            }
        }

        private void ReadFieldIntrospection(ResourceIntrospectionManifest.ResourceDiskStruct refStruct, ResourceIntrospectionManifest.ResourceDiskStruct.Field field, KVObject structEntry)
        {
            SpecialFields.TryGetValue((refStruct.Name, field.FieldName), out var special);

            if (special.Behavior == SpecialFieldBehavior.Discard)
            {
                return;
            }

            var asBlob = special.Behavior == SpecialFieldBehavior.BinaryBlob;

            KVObject fieldValue;

            if (field.Type == SchemaFieldType.Char && field.Count > 1 && field.Indirections.Count == 0)
            {
                // Inline char arrays are written as a single string offset, the count is only the buffer capacity
                fieldValue = Reader.ReadOffsetString(Encoding.UTF8);
            }
            else if (special.Behavior == SpecialFieldBehavior.Rows && field.Indirections.Count == 0 && field.Count % special.Columns == 0)
            {
                fieldValue = KVObject.Array();

                for (var row = 0; row < field.Count / special.Columns; row++)
                {
                    fieldValue.Add(ReadElements(field, 0, special.Columns, asBlob));
                }
            }
            else if (field.Count > 1)
            {
                // Fixed size arrays repeat the whole value inline, including any indirections
                fieldValue = ReadElements(field, 0, field.Count, asBlob);
            }
            else
            {
                fieldValue = ReadIndirection(field, 0, asBlob);
            }

            structEntry.Add(field.FieldName, fieldValue);
        }

        /// <summary>
        /// Reads the value at the given indirection level, each level wraps all the levels after it.
        /// Leaves the reader right after the value at this level.
        /// </summary>
        private KVObject ReadIndirection(ResourceIntrospectionManifest.ResourceDiskStruct.Field field, int level, bool asBlob)
        {
            if (level == field.Indirections.Count)
            {
                return ReadField(field);
            }

            var indirection = (SchemaIndirectionType)field.Indirections[level];
            var slotOffset = Reader.BaseStream.Position;

            switch (indirection)
            {
                case SchemaIndirectionType.ResourcePointer:
                {
                    var offset = Reader.ReadInt32();

                    if (offset == 0)
                    {
                        return KVObject.Null();
                    }

                    Reader.BaseStream.Position = slotOffset + offset;

                    var value = ReadIndirection(field, level + 1, asBlob);

                    Reader.BaseStream.Position = slotOffset + 4;

                    return value;
                }

                case SchemaIndirectionType.ResourceArray:
                {
                    var offset = Reader.ReadInt32();
                    var count = Reader.ReadInt32();

                    Reader.BaseStream.Position = slotOffset + offset;

                    var value = ReadElements(field, level + 1, count, asBlob);

                    Reader.BaseStream.Position = slotOffset + 8;

                    return value;
                }

                default:
                    throw new UnexpectedMagicException("Unsupported indirection", (int)indirection, nameof(indirection));
            }
        }

        private KVObject ReadElements(ResourceIntrospectionManifest.ResourceDiskStruct.Field field, int level, int count, bool asBlob)
        {
            if (level == field.Indirections.Count && (asBlob || field.Type is SchemaFieldType.Byte or SchemaFieldType.UChar))
            {
                var size = field.Type == SchemaFieldType.Color ? 4 : 1;

                return KVObject.Blob(Reader.ReadBytes(count * size));
            }

            var values = KVObject.Array();

            for (var i = 0; i < count; i++)
            {
                values.Add(ReadIndirection(field, level, asBlob));
            }

            return values;
        }

        private KVObject ReadField(ResourceIntrospectionManifest.ResourceDiskStruct.Field field)
        {
            Debug.Assert(IntrospectionManifest != null);

            switch (field.Type)
            {
                case SchemaFieldType.Struct:
                    var newStruct = IntrospectionManifest.GetStructById(field.TypeData)
                        ?? throw new InvalidDataException($"Could not find struct {field.TypeData} for field '{field.FieldName}'.");
                    return ReadStructure(newStruct, Reader.BaseStream.Position);

                case SchemaFieldType.Enum:
                {
                    // The type data is the hash of the enum name, which is what the enum introspection is keyed by
                    var enumValue = Reader.ReadInt32();
                    var enumeratorName = IntrospectionManifest.GetEnumValueName(field.TypeData, enumValue);

                    if (enumeratorName != null)
                    {
                        return enumeratorName;
                    }

                    // Flag combinations and values the manifest does not name stay numeric
                    return enumValue;
                }

                // A char that is not an inline char array is a single character
                case SchemaFieldType.Char:
                case SchemaFieldType.SByte:
                    return (int)Reader.ReadSByte();

                case SchemaFieldType.UChar:
                case SchemaFieldType.Byte:
                    return (uint)Reader.ReadByte();

                case SchemaFieldType.Boolean:
                    return Reader.ReadBoolean();

                case SchemaFieldType.Int16:
                    return (int)Reader.ReadInt16(); // TODO: Could actually be int16

                case SchemaFieldType.UInt16:
                    return (uint)Reader.ReadUInt16(); // TODO: Could actually be uint16

                case SchemaFieldType.Int:
                case SchemaFieldType.Int32:
                    return Reader.ReadInt32();

                case SchemaFieldType.UInt:
                case SchemaFieldType.UInt32:
                    return (uint)Reader.ReadUInt32();

                case SchemaFieldType.Float_8:
                case SchemaFieldType.Float:
                    return (double)Reader.ReadSingle(); // TODO: Could actually be float

                case SchemaFieldType.Double:
                case SchemaFieldType.Float64:
                    return Reader.ReadDouble();

                case SchemaFieldType.Int64:
                    return Reader.ReadInt64();

                // Type 41 is a deferred resource reference, stored the same way as an external reference
                case SchemaFieldType.ExternalReference:
                case SchemaFieldType.Unused:
                    var id = Reader.ReadUInt64();
                    var value = id > 0 ? Resource.ExternalReferences?[id] : null;

                    if (value == null)
                    {
                        return KVObject.Null();
                    }

                    KVObject resourceValue = value;
                    resourceValue.Flag = KVFlag.Resource;
                    return resourceValue;

                case SchemaFieldType.UInt64:
                    return (ulong)Reader.ReadUInt64();

                case SchemaFieldType.Vector3D:
                case SchemaFieldType.QAngle:
                case SchemaFieldType.RadianEuler:
                case SchemaFieldType.DegreeEuler:
                case SchemaFieldType.RotationVector:
                {
                    var arrayObject = KVObject.Array();
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    return arrayObject;
                }

                case SchemaFieldType.Quaternion:
                case SchemaFieldType.Fltx4:
                case SchemaFieldType.Vector4D:
                {
                    var arrayObject = KVObject.Array();
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    return arrayObject;
                }

                case SchemaFieldType.Color:
                {
                    var arrayObject = KVObject.Array();
                    arrayObject.Add((int)Reader.ReadByte());
                    arrayObject.Add((int)Reader.ReadByte());
                    arrayObject.Add((int)Reader.ReadByte());
                    arrayObject.Add((int)Reader.ReadByte());
                    return arrayObject;
                }

                case SchemaFieldType.UniqueId:
                    return KVObject.Blob(Reader.ReadBytes(16));

                case SchemaFieldType.ResourceString:
                    return Reader.ReadOffsetString(Encoding.UTF8);

                case SchemaFieldType.Vector2D:
                {
                    var arrayObject = KVObject.Array();
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    return arrayObject;
                }

                // FourVectors stays in its structure of arrays order: x[4], y[4], z[4]
                case SchemaFieldType.Matrix3x4:
                case SchemaFieldType.Matrix3x4a:
                case SchemaFieldType.FourVectors:
                {
                    var arrayObject = KVObject.Array();
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    return arrayObject;
                }

                case SchemaFieldType.VMatrix:
                    return ReadFloats(16);

                // Mins followed by maxs
                case SchemaFieldType.AABB:
                    return ReadFloats(6);

                case SchemaFieldType.Transform:
                {
                    var arrayObject = KVObject.Array();
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    arrayObject.Add(Reader.ReadSingle());
                    return arrayObject;
                }

                default:
                    throw new NotImplementedException($"Unknown data type: {field.Type} (name: {field.FieldName})");
            }
        }

        private KVObject ReadFloats(int count)
        {
            var arrayObject = KVObject.Array();

            for (var i = 0; i < count; i++)
            {
                arrayObject.Add(Reader.ReadSingle());
            }

            return arrayObject;
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Converts this <see cref="NTRO"/> block's data to KV3 format and writes it as text.
        /// </remarks>
        public override void WriteText(IndentedTextWriter writer)
        {
            Output.ToKV3Document().WriteKV3Text(writer);
        }
    }
}
