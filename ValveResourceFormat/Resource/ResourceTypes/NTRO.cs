using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class NTRO : ResourceData
    {
        public KVObject Output { get; private set; }
        public string StructName { get; init; }

        private BinaryReader Reader;
        private ResourceIntrospectionManifest IntrospectionManifest;

        public override void Read(BinaryReader reader)
        {
            Reader = reader;
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
                Reader = null;
                Resource = null;
                IntrospectionManifest = null;
            }
        }

        private KVObject ReadStructure(ResourceIntrospectionManifest.ResourceDiskStruct refStruct, long startingOffset)
        {
            var structEntry = new KVObject(refStruct.Name);

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
            var indirection = SchemaIndirectionType.Unknown;

            if (count == 0)
            {
                count = 1;
            }

            long prevOffset = 0;

            if (field.Indirections.Count > 0)
            {
                // TODO
                if (field.Indirections.Count > 1)
                {
                    throw new NotImplementedException("More than one indirection, not yet handled.");
                }

                // TODO
                if (field.Count > 0)
                {
                    throw new NotImplementedException("Indirection.Count > 0 && field.Count > 0");
                }

                indirection = (SchemaIndirectionType)field.Indirections[0]; // TODO: depth needs fixing?

                var offset = Reader.ReadUInt32();

                if (indirection == SchemaIndirectionType.ResourcePointer)
                {
                    if (offset == 0)
                    {
                        structEntry.AddProperty(field.FieldName, new KVValue(KVType.NULL, null)); // :shrug:

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

            KVValue fieldValue = null;

            if (field.Count > 0 || indirection == SchemaIndirectionType.ResourceArray)
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
                    fieldValue = new KVValue(KVType.BINARY_BLOB, Reader.ReadBytes((int)count / size));
                }
                else
                {
                    //var ntroValues = new NTROArray(field.Type, (int)count, pointer, field.Indirections.Count > 0);
                    var ntroValues = new KVObject(field.FieldName, isArray: true, capacity: (int)count);

                    for (var i = 0; i < count; i++)
                    {
                        ntroValues.AddProperty(null, ReadField(field));
                    }

                    fieldValue = new KVValue(KVType.ARRAY, ntroValues);
                }
            }
            else
            {
                Debug.Assert(count == 1 && field.Count == 0);
                fieldValue = ReadField(field);
            }

            structEntry.AddProperty(field.FieldName, fieldValue);

            if (prevOffset > 0)
            {
                Reader.BaseStream.Position = prevOffset;
            }
        }

        private KVValue ReadField(ResourceIntrospectionManifest.ResourceDiskStruct.Field field)
        {
            switch (field.Type)
            {
                case SchemaFieldType.Struct:
                    var newStruct = IntrospectionManifest.ReferencedStructs.First(x => x.Id == field.TypeData);
                    return BinaryKV3.MakeValue(KVType.OBJECT, ReadStructure(newStruct, Reader.BaseStream.Position));

                case SchemaFieldType.Enum:
                    // TODO: Lookup in ReferencedEnums
                    return BinaryKV3.MakeValue(KVType.UINT32, Reader.ReadUInt32());

                case SchemaFieldType.SByte:
                    return BinaryKV3.MakeValue(KVType.INT32, (int)Reader.ReadSByte());

                case SchemaFieldType.Byte:
                    return BinaryKV3.MakeValue(KVType.UINT32, (uint)Reader.ReadByte());

                case SchemaFieldType.Boolean:
                    return BinaryKV3.MakeValue(KVType.BOOLEAN, Reader.ReadBoolean());

                case SchemaFieldType.Int16:
                    return BinaryKV3.MakeValue(KVType.INT32, (int)Reader.ReadInt16());

                case SchemaFieldType.UInt16:
                    return BinaryKV3.MakeValue(KVType.UINT32, (uint)Reader.ReadUInt16());

                case SchemaFieldType.Int32:
                    return BinaryKV3.MakeValue(KVType.INT32, Reader.ReadInt32());

                case SchemaFieldType.UInt32:
                    return BinaryKV3.MakeValue(KVType.UINT32, Reader.ReadUInt32());

                case SchemaFieldType.Float:
                    return BinaryKV3.MakeValue(KVType.FLOAT, (double)Reader.ReadSingle());

                case SchemaFieldType.Int64:
                    return BinaryKV3.MakeValue(KVType.INT64, Reader.ReadInt64());

                case SchemaFieldType.ExternalReference:
                    var id = Reader.ReadUInt64();
                    var value = id > 0
                        ? Resource.ExternalReferences?.ResourceRefInfoList.FirstOrDefault(c => c.Id == id)?.Name
                        : null;

                    if (value == null)
                    {
                        return BinaryKV3.MakeValue(KVType.NULL, null);
                    }

                    return BinaryKV3.MakeValue(KVType.STRING, value, KVFlag.ResourceName);

                case SchemaFieldType.UInt64:
                    return BinaryKV3.MakeValue(KVType.UINT64, Reader.ReadUInt64());

                case SchemaFieldType.Vector3D:
                    {
                        var arrayObject = new KVObject(field.Type.ToString(), isArray: true);
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        return BinaryKV3.MakeValue(KVType.ARRAY, arrayObject);
                    }

                case SchemaFieldType.Quaternion:
                case SchemaFieldType.Fltx4:
                case SchemaFieldType.Vector4D:
                case SchemaFieldType.FourVectors:
                    {
                        var arrayObject = new KVObject(field.Type.ToString(), isArray: true);
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        return BinaryKV3.MakeValue(KVType.ARRAY, arrayObject);
                    }

                case SchemaFieldType.Color:
                    {
                        var arrayObject = new KVObject(field.Type.ToString(), isArray: true);
                        arrayObject.AddProperty(null, BinaryKV3.MakeValue(KVType.INT32, (int)Reader.ReadByte()));
                        arrayObject.AddProperty(null, BinaryKV3.MakeValue(KVType.INT32, (int)Reader.ReadByte()));
                        arrayObject.AddProperty(null, BinaryKV3.MakeValue(KVType.INT32, (int)Reader.ReadByte()));
                        arrayObject.AddProperty(null, BinaryKV3.MakeValue(KVType.INT32, (int)Reader.ReadByte()));
                        return BinaryKV3.MakeValue(KVType.ARRAY, arrayObject);
                    }

                case SchemaFieldType.Char:
                    return BinaryKV3.MakeValue(KVType.STRING, Reader.ReadOffsetString(Encoding.UTF8));

                case SchemaFieldType.ResourceString:
                    return BinaryKV3.MakeValue(KVType.STRING, Reader.ReadOffsetString(Encoding.UTF8));

                case SchemaFieldType.Vector2D:
                    {
                        var arrayObject = new KVObject(field.Type.ToString(), isArray: true);
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        return BinaryKV3.MakeValue(KVType.ARRAY, arrayObject);
                    }

                case SchemaFieldType.Matrix3x4:
                case SchemaFieldType.Matrix3x4a:
                    {
                        var arrayObject = new KVObject(field.Type.ToString(), isArray: true);
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        return BinaryKV3.MakeValue(KVType.ARRAY, arrayObject);
                    }

                case SchemaFieldType.Transform:
                    {
                        var arrayObject = new KVObject(field.Type.ToString(), isArray: true);
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        return BinaryKV3.MakeValue(KVType.ARRAY, arrayObject);
                    }

                default:
                    throw new NotImplementedException($"Unknown data type: {field.Type} (name: {field.FieldName})");
            }
        }

        public override string ToString()
        {
            return new KV3File(Output).ToString() ?? "Nope.";
        }
    }
}
