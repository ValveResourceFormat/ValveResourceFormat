using System;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.NTRO;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes
{
    public class NTRO : ResourceData
    {
        protected BinaryReader Reader { get; private set; }
        protected Resource Resource { get; private set; }
        public NTROStruct Output { get; private set; }
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

        private NTROStruct ReadStructure(ResourceIntrospectionManifest.ResourceDiskStruct refStruct, long startingOffset)
        {
            var structEntry = new NTROStruct(refStruct.Name);

            foreach (var field in refStruct.FieldIntrospection)
            {
                Reader.BaseStream.Position = startingOffset + field.OnDiskOffset;

                ReadFieldIntrospection(field, ref structEntry);
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

                    ReadFieldIntrospection(field, ref structEntry);
                }

                Reader.BaseStream.Position = previousOffset;
            }

            return structEntry;
        }

        private void ReadFieldIntrospection(ResourceIntrospectionManifest.ResourceDiskStruct.Field field, ref NTROStruct structEntry)
        {
            var count = (uint)field.Count;
            var pointer = false; // TODO: get rid of this

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
                    pointer = true;

                    if (offset == 0)
                    {
                        structEntry.Add(field.FieldName, new NTROValue<byte?>(field.Type, null, true)); //being byte shouldn't matter

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
                    var ntroValues = new NTROValue<byte[]>(field.Type, Reader.ReadBytes((int)count), pointer);
                    structEntry.Add(field.FieldName, ntroValues);
                }
                else
                {
                    var ntroValues = new NTROArray(field.Type, (int)count, pointer, field.Indirections.Count > 0);

                    for (var i = 0; i < count; i++)
                    {
                        ntroValues[i] = ReadField(field, pointer);
                    }

                    structEntry.Add(field.FieldName, ntroValues);
                }
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    structEntry.Add(field.FieldName, ReadField(field, pointer));
                }
            }

            if (prevOffset > 0)
            {
                Reader.BaseStream.Position = prevOffset;
            }
        }

        private NTROValue ReadField(ResourceIntrospectionManifest.ResourceDiskStruct.Field field, bool pointer)
        {
            switch (field.Type)
            {
                case SchemaFieldType.Struct:
                    var newStruct = Resource.IntrospectionManifest.ReferencedStructs.First(x => x.Id == field.TypeData);
                    return new NTROValue<NTROStruct>(field.Type, ReadStructure(newStruct, Reader.BaseStream.Position), pointer);

                case SchemaFieldType.Enum:
                {
                    var enumData = Resource.IntrospectionManifest.ReferencedEnums.FirstOrDefault(x => x.Id == field.TypeData);
                    if (enumData != null)
                    {
                        return new NTROValue<string>(field.Type, enumData.EnumValueIntrospection[(int)Reader.ReadUInt32()].EnumValueName, pointer);
                    }
                    return new NTROValue<uint>(field.Type, Reader.ReadUInt32(), pointer);
                }

                case SchemaFieldType.SByte:
                    return new NTROValue<sbyte>(field.Type, Reader.ReadSByte(), pointer);

                case SchemaFieldType.Byte:
                    return new NTROValue<byte>(field.Type, Reader.ReadByte(), pointer);

                case SchemaFieldType.Boolean:
                    return new NTROValue<bool>(field.Type, Reader.ReadByte() == 1, pointer);

                case SchemaFieldType.Int16:
                    return new NTROValue<short>(field.Type, Reader.ReadInt16(), pointer);

                case SchemaFieldType.UInt16:
                    return new NTROValue<ushort>(field.Type, Reader.ReadUInt16(), pointer);

                case SchemaFieldType.Int32:
                    return new NTROValue<int>(field.Type, Reader.ReadInt32(), pointer);

                case SchemaFieldType.UInt32:
                    return new NTROValue<uint>(field.Type, Reader.ReadUInt32(), pointer);

                case SchemaFieldType.Float:
                    return new NTROValue<float>(field.Type, Reader.ReadSingle(), pointer);

                case SchemaFieldType.Int64:
                    return new NTROValue<long>(field.Type, Reader.ReadInt64(), pointer);

                case SchemaFieldType.ExternalReference:
                    var id = Reader.ReadUInt64();
                    var value = id > 0
                        ? Resource.ExternalReferences?.ResourceRefInfoList.FirstOrDefault(c => c.Id == id)?.Name
                        : null;

                    return new NTROValue<string>(field.Type, value, pointer);

                case SchemaFieldType.UInt64:
                    return new NTROValue<ulong>(field.Type, Reader.ReadUInt64(), pointer);

                case SchemaFieldType.Vector3D:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case SchemaFieldType.Quaternion:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case SchemaFieldType.Color:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<byte>(SchemaFieldType.Byte, Reader.ReadByte(), pointer),
                            new NTROValue<byte>(SchemaFieldType.Byte, Reader.ReadByte(), pointer),
                            new NTROValue<byte>(SchemaFieldType.Byte, Reader.ReadByte(), pointer),
                            new NTROValue<byte>(SchemaFieldType.Byte, Reader.ReadByte(), pointer)),
                        pointer);

                case SchemaFieldType.Fltx4:
                case SchemaFieldType.Vector4D:
                case SchemaFieldType.FourVectors:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case SchemaFieldType.Char:
                case SchemaFieldType.ResourceString:
                    return new NTROValue<string>(field.Type, Reader.ReadOffsetString(Encoding.UTF8), pointer);

                case SchemaFieldType.Vector2D:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case SchemaFieldType.Matrix3x4:
                case SchemaFieldType.Matrix3x4a:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case SchemaFieldType.Transform:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(SchemaFieldType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

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
