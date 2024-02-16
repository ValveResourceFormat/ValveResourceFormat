using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Serialization.NTRO;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class NTRO : ResourceData
    {
        protected BinaryReader Reader { get; private set; }
        protected Resource Resource { get; private set; }
        public KVObject Output { get; private set; }
        public string StructName { get; set; }

        public override void Read(BinaryReader reader, Resource resource)
        {
            Reader = reader;
            Resource = resource;

            if (StructName != null)
            {
                var refStruct = resource.IntrospectionManifest.ReferencedStructs.Find(s => s.Name == StructName);

                Output = ReadStructure(refStruct, Offset);

                return;
            }

            foreach (var refStruct in resource.IntrospectionManifest.ReferencedStructs)
            {
                Output = ReadStructure(refStruct, Offset);

                break;
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

                var newStruct = Resource.IntrospectionManifest.ReferencedStructs.First(x => x.Id == refStruct.BaseStructId);

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

                var indirection = (SchemaIndirectionType)field.Indirections[0]; // TODO: depth needs fixing?

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

            //if (pointer)
            //{
            //    Writer.Write("{0} {1}* = (ptr) ->", ValveDataType(field.Type), field.FieldName);
            //}
            if (field.Count > 0 || field.Indirections.Count > 0)
            {
                if (field.Type == SchemaFieldType.Byte)
                {
                    //special case for byte arrays for faster access
                    structEntry.AddProperty(field.FieldName, new KVValue(KVType.BINARY_BLOB, Reader.ReadBytes((int)count)));
                }
                else
                {
                    //var ntroValues = new NTROArray(field.Type, (int)count, pointer, field.Indirections.Count > 0);
                    var ntroValues = new KVObject(field.FieldName, isArray: true, capacity: (int)count);

                    for (var i = 0; i < count; i++)
                    {
                        ntroValues.AddProperty(null, ReadField(field));
                    }

                    structEntry.AddProperty(field.FieldName, new KVValue(KVType.ARRAY, ntroValues));
                }
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    structEntry.AddProperty(field.FieldName, ReadField(field));
                }
            }

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
                    var newStruct = Resource.IntrospectionManifest.ReferencedStructs.First(x => x.Id == field.TypeData);
                    return new KVValue(KVType.OBJECT, ReadStructure(newStruct, Reader.BaseStream.Position));

                case SchemaFieldType.Enum:
                    // TODO: Lookup in ReferencedEnums
                    return new KVValue(KVType.UINT32, Reader.ReadUInt32());

                case SchemaFieldType.SByte:
                    return new KVValue(KVType.INT32, (int)Reader.ReadSByte());

                case SchemaFieldType.Byte:
                    return new KVValue(KVType.UINT32, (uint)Reader.ReadByte());

                case SchemaFieldType.Boolean:
                    return new KVValue(KVType.BOOLEAN, Reader.ReadBoolean());

                case SchemaFieldType.Int16:
                    return new KVValue(KVType.INT32, (int)Reader.ReadInt16());

                case SchemaFieldType.UInt16:
                    return new KVValue(KVType.UINT32, Reader.ReadUInt16());

                case SchemaFieldType.Int32:
                    return new KVValue(KVType.INT32, Reader.ReadInt32());

                case SchemaFieldType.UInt32:
                    return new KVValue(KVType.UINT32, Reader.ReadUInt32());

                case SchemaFieldType.Float:
                    return new KVValue(KVType.FLOAT, Reader.ReadSingle());

                case SchemaFieldType.Int64:
                    return new KVValue(KVType.INT64, Reader.ReadInt64());

                case SchemaFieldType.ExternalReference:
                    var id = Reader.ReadUInt64();
                    var value = id > 0
                        ? Resource.ExternalReferences?.ResourceRefInfoList.FirstOrDefault(c => c.Id == id)?.Name
                        : null;

                    return new KVFlaggedValue(KVType.STRING, KVFlag.ResourceName, value);

                case SchemaFieldType.UInt64:
                    return new KVValue(KVType.UINT64, Reader.ReadUInt64());

                case SchemaFieldType.Vector3D:
                    {
                        var arrayObject = new KVObject(field.Type.ToString(), isArray: true);
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        return new KVValue(KVType.ARRAY, arrayObject);
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
                        return new KVValue(KVType.ARRAY, arrayObject);
                    }

                case SchemaFieldType.Color:
                    {
                        var arrayObject = new KVObject(field.Type.ToString(), isArray: true);
                        arrayObject.AddProperty(null, new KVValue(KVType.INT32, (int)Reader.ReadByte()));
                        arrayObject.AddProperty(null, new KVValue(KVType.INT32, (int)Reader.ReadByte()));
                        arrayObject.AddProperty(null, new KVValue(KVType.INT32, (int)Reader.ReadByte()));
                        arrayObject.AddProperty(null, new KVValue(KVType.INT32, (int)Reader.ReadByte()));
                        return new KVValue(KVType.ARRAY, arrayObject);
                    }

                case SchemaFieldType.Char:
                    return new KVValue(KVType.STRING, Reader.ReadOffsetString(Encoding.UTF8));

                case SchemaFieldType.ResourceString:
                    return new KVFlaggedValue(KVType.STRING, KVFlag.Resource, Reader.ReadOffsetString(Encoding.UTF8));

                case SchemaFieldType.Vector2D:
                    {
                        var arrayObject = new KVObject(field.Type.ToString(), isArray: true);
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        arrayObject.AddProperty(null, new KVValue(KVType.FLOAT, Reader.ReadSingle()));
                        return new KVValue(KVType.ARRAY, arrayObject);
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
                        return new KVValue(KVType.ARRAY, arrayObject);
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
                        return new KVValue(KVType.ARRAY, arrayObject);
                    }

                default:
                    throw new NotImplementedException($"Unknown data type: {field.Type} (name: {field.FieldName})");
            }
        }

        public override string ToString()
        {
            return Output?.ToString() ?? "Nope.";
        }
    }
}
