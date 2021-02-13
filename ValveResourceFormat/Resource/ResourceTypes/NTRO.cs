using System;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization.NTRO;

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

                var indirection = field.Indirections[0]; // TODO: depth needs fixing?

                var offset = Reader.ReadUInt32();

                if (indirection == 0x03)
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
                else if (indirection == 0x04)
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
                    throw new NotImplementedException(string.Format("Unknown indirection. ({0})", indirection));
                }
            }

            //if (pointer)
            //{
            //    Writer.Write("{0} {1}* = (ptr) ->", ValveDataType(field.Type), field.FieldName);
            //}
            if (field.Count > 0 || field.Indirections.Count > 0)
            {
                if (field.Type == DataType.Byte)
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
                case DataType.Struct:
                    var newStruct = Resource.IntrospectionManifest.ReferencedStructs.First(x => x.Id == field.TypeData);
                    return new NTROValue<NTROStruct>(field.Type, ReadStructure(newStruct, Reader.BaseStream.Position), pointer);

                case DataType.Enum:
                    // TODO: Lookup in ReferencedEnums
                    return new NTROValue<uint>(field.Type, Reader.ReadUInt32(), pointer);

                case DataType.SByte:
                    return new NTROValue<sbyte>(field.Type, Reader.ReadSByte(), pointer);

                case DataType.Byte:
                    return new NTROValue<byte>(field.Type, Reader.ReadByte(), pointer);

                case DataType.Boolean:
                    return new NTROValue<bool>(field.Type, Reader.ReadByte() == 1 ? true : false, pointer);

                case DataType.Int16:
                    return new NTROValue<short>(field.Type, Reader.ReadInt16(), pointer);

                case DataType.UInt16:
                    return new NTROValue<ushort>(field.Type, Reader.ReadUInt16(), pointer);

                case DataType.Int32:
                    return new NTROValue<int>(field.Type, Reader.ReadInt32(), pointer);

                case DataType.UInt32:
                    return new NTROValue<uint>(field.Type, Reader.ReadUInt32(), pointer);

                case DataType.Float:
                    return new NTROValue<float>(field.Type, Reader.ReadSingle(), pointer);

                case DataType.Int64:
                    return new NTROValue<long>(field.Type, Reader.ReadInt64(), pointer);

                case DataType.ExternalReference:
                    var id = Reader.ReadUInt64();
                    var value = id > 0
                        ? Resource.ExternalReferences?.ResourceRefInfoList.FirstOrDefault(c => c.Id == id)?.Name
                        : null;

                    return new NTROValue<string>(field.Type, value, pointer);

                case DataType.UInt64:
                    return new NTROValue<ulong>(field.Type, Reader.ReadUInt64(), pointer);

                case DataType.Vector:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case DataType.Quaternion:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case DataType.Color:
                case DataType.Fltx4:
                case DataType.Vector4D:
                case DataType.Vector4D_44:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case DataType.String4:
                case DataType.String:
                    return new NTROValue<string>(field.Type, Reader.ReadOffsetString(Encoding.UTF8), pointer);

                case DataType.Matrix2x4:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case DataType.Matrix3x4:
                case DataType.Matrix3x4a:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer)),
                        pointer);

                case DataType.CTransform:
                    return new NTROValue<NTROStruct>(
                        field.Type,
                        new NTROStruct(
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer),
                            new NTROValue<float>(DataType.Float, Reader.ReadSingle(), pointer)),
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
