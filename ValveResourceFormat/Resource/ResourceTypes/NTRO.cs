using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a resource with introspection data.
    /// </summary>
    public class NTRO : Block
    {
        /// <summary>
        /// Gets the output data.
        /// </summary>
        public KVObject Output { get; private set; }
        /// <summary>
        /// Gets or sets the struct name.
        /// </summary>
        public string StructName { get; init; }

        private BinaryReader Reader => Resource.Reader;
        private ResourceIntrospectionManifest IntrospectionManifest;

        /// <inheritdoc/>
        public override BlockType Type => BlockType.DATA;

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            IntrospectionManifest = (ResourceIntrospectionManifest)Resource.GetBlockByType(BlockType.NTRO);

            try
            {
                if (StructName != null)
                {
                    var refStruct = IntrospectionManifest.ReferencedStructs.Find(s => s.Name == StructName);

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
            var structEntry = KVObject.Collection();

            foreach (var field in refStruct.FieldIntrospection)
            {
                Reader.BaseStream.Position = startingOffset + field.OnDiskOffset;

                ReadFieldIntrospection(field, structEntry);
            }

            // Some structs are padded, so all the field sizes do not add up to the size on disk
            Reader.BaseStream.Position = startingOffset + refStruct.DiskSize;

            if (refStruct.BaseStructId != 0)
            {
                var previousOffset = Reader.BaseStream.Position;

                var newStruct = IntrospectionManifest.ReferencedStructs.First(x => x.Id == refStruct.BaseStructId);

                // Valve doesn't print this struct's type, so we can't just call ReadStructure *sigh*
                foreach (var field in newStruct.FieldIntrospection)
                {
                    Reader.BaseStream.Position = startingOffset + field.OnDiskOffset;

                    ReadFieldIntrospection(field, structEntry);
                }

                Reader.BaseStream.Position = previousOffset;
            }

            return structEntry;
        }

        private void ReadFieldIntrospection(ResourceIntrospectionManifest.ResourceDiskStruct.Field field, KVObject structEntry)
        {
            var count = (uint)field.Count;

            if (count == 0)
            {
                count = 1;
            }

            long prevOffset = 0;

            if (field.Indirections.Count == 1)
            {
                var indirection = (SchemaIndirectionType)field.Indirections[0];

                var offset = Reader.ReadUInt32();

                if (indirection == SchemaIndirectionType.ResourcePointer)
                {
                    if (offset == 0)
                    {
                        structEntry.Add(field.FieldName, KVObject.Null()); // :shrug:

                        return;
                    }

                    prevOffset = Reader.BaseStream.Position;

                    Reader.BaseStream.Position += offset - 4;
                }
                else if (indirection == SchemaIndirectionType.ResourceArray)
                {
                    count = Reader.ReadUInt32();

                    prevOffset = Reader.BaseStream.Position;

                    if (count > 0)
                    {
                        Reader.BaseStream.Position += offset - 8;
                    }
                }
                else
                {
                    throw new UnexpectedMagicException("Unsupported indirection", (int)indirection, nameof(indirection));
                }
            }
            else if (field.Indirections.Count == 2)
            {
                var indirection0 = (SchemaIndirectionType)field.Indirections[0];
                var indirection1 = (SchemaIndirectionType)field.Indirections[1];

                if (indirection0 == SchemaIndirectionType.ResourceArray && indirection1 == SchemaIndirectionType.ResourcePointer)
                {
                    var arrayOffset = Reader.ReadUInt32();
                    var arrayCount = Reader.ReadUInt32();

                    prevOffset = Reader.BaseStream.Position;

                    if (arrayCount == 0)
                    {
                        structEntry.Add(field.FieldName, KVObject.Array());
                        return;
                    }

                    Reader.BaseStream.Position += arrayOffset - 8;

                    // Array of pointers
                    var arrayValues = KVObject.Array();

                    for (var i = 0; i < arrayCount; i++)
                    {
                        var pointerOffset = Reader.ReadUInt32();

                        if (pointerOffset == 0)
                        {
                            arrayValues.Add(KVObject.Null());
                        }
                        else
                        {
                            var pointerPrevOffset = Reader.BaseStream.Position;
                            Reader.BaseStream.Position += pointerOffset - 4;

                            arrayValues.Add(ReadField(field));

                            Reader.BaseStream.Position = pointerPrevOffset;
                        }
                    }

                    structEntry.Add(field.FieldName, arrayValues);
                    Reader.BaseStream.Position = prevOffset;

                    return;
                }
                else
                {
                    throw new NotImplementedException($"Unsupported 2-level indirection: {indirection0}, {indirection1}");
                }
            }
            else if (field.Indirections.Count > 2)
            {
                throw new NotImplementedException($"More than 2 levels of indirection not supported (found {field.Indirections.Count})");
            }

            KVObject fieldValue;

            if (field.Count > 0 || field.Indirections.Count == 1 && (SchemaIndirectionType)field.Indirections[0] == SchemaIndirectionType.ResourceArray)
            {
                if (field.Type == SchemaFieldType.Byte || field.Type == SchemaFieldType.Color)
                {
                    var size = field.Type switch
                    {
                        SchemaFieldType.Byte => 1,
                        SchemaFieldType.Color => 4,
                        _ => 0,
                    };

                    //special case for byte arrays for faster access
                    fieldValue = KVObject.Blob(Reader.ReadBytes((int)count / size));
                }
                else
                {
                    //var ntroValues = new NTROArray(field.Type, (int)count, pointer, field.Indirections.Count > 0);
                    var ntroValues = KVObject.Array();

                    for (var i = 0; i < count; i++)
                    {
                        ntroValues.Add(ReadField(field));
                    }

                    fieldValue = ntroValues;
                }
            }
            else
            {
                Debug.Assert(count == 1 && field.Count == 0);
                fieldValue = ReadField(field);
            }

            structEntry.Add(field.FieldName, fieldValue);

            if (prevOffset > 0)
            {
                Reader.BaseStream.Position = prevOffset;
            }
        }

        private KVObject ReadField(ResourceIntrospectionManifest.ResourceDiskStruct.Field field)
        {
            switch (field.Type)
            {
                case SchemaFieldType.Struct:
                    var newStruct = IntrospectionManifest.ReferencedStructs.First(x => x.Id == field.TypeData);
                    return ReadStructure(newStruct, Reader.BaseStream.Position);

                case SchemaFieldType.Enum:
                    // TODO: Lookup in ReferencedEnums
                    return (uint)Reader.ReadUInt32();

                case SchemaFieldType.SByte:
                    return (int)Reader.ReadSByte();

                case SchemaFieldType.Byte:
                    return (uint)Reader.ReadByte();

                case SchemaFieldType.Boolean:
                    return Reader.ReadBoolean();

                case SchemaFieldType.Int16:
                    return (int)Reader.ReadInt16(); // TODO: Could actually be int16

                case SchemaFieldType.UInt16:
                    return (uint)Reader.ReadUInt16(); // TODO: Could actually be uint16

                case SchemaFieldType.Int32:
                    return Reader.ReadInt32();

                case SchemaFieldType.UInt32:
                    return (uint)Reader.ReadUInt32();

                case SchemaFieldType.Float:
                    return (double)Reader.ReadSingle(); // TODO: Could actually be float

                case SchemaFieldType.Int64:
                    return Reader.ReadInt64();

                case SchemaFieldType.ExternalReference:
                    var id = Reader.ReadUInt64();
                    var value = id > 0
                        ? Resource.ExternalReferences?.ResourceRefInfoList.FirstOrDefault(c => c.Id == id)?.Name
                        : null;

                    if (value == null)
                    {
                        return KVObject.Null();
                    }

                    KVObject resourceNameValue = value;
                    resourceNameValue.Flag = KVFlag.ResourceName;
                    return resourceNameValue;

                case SchemaFieldType.UInt64:
                    return (ulong)Reader.ReadUInt64();

                case SchemaFieldType.Vector3D:
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
                case SchemaFieldType.FourVectors:
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

                case SchemaFieldType.Char:
                    return Reader.ReadOffsetString(Encoding.UTF8);

                case SchemaFieldType.ResourceString:
                    KVObject resourceValue = Reader.ReadOffsetString(Encoding.UTF8);
                    resourceValue.Flag = KVFlag.Resource;
                    return resourceValue;

                case SchemaFieldType.Vector2D:
                    {
                        var arrayObject = KVObject.Array();
                        arrayObject.Add(Reader.ReadSingle());
                        arrayObject.Add(Reader.ReadSingle());
                        return arrayObject;
                    }

                case SchemaFieldType.Matrix3x4:
                case SchemaFieldType.Matrix3x4a:
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
