using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class NTRO : ResourceData
    {
        private BinaryReader Reader;
        private Resource Resource;
        private IndentedTextWriter Writer;
        private string Output;

        public override void Read(BinaryReader reader, Resource resource)
        {
            Reader = reader;
            Resource = resource;

            using (var output = new StringWriter())
            using (var writer = new IndentedTextWriter(output, "\t"))
            {
                Writer = writer;

                foreach (var refStruct in resource.IntrospectionManifest.ReferencedStructs)
                {
                    ReadStructure(refStruct, this.Offset);

                    break;
                }

                Output = output.ToString();
            }
        }

        private void ReadStructure(ResourceIntrospectionManifest.ResourceDiskStruct refStruct, long startingOffset)
        {
            Writer.WriteLine(refStruct.Name);
            Writer.WriteLine("{");
            Writer.Indent++;



            foreach (var field in refStruct.FieldIntrospection)
            {
                Reader.BaseStream.Position = startingOffset + field.OnDiskOffset;

                ReadFieldIntrospection(field);
            }

            if (refStruct.BaseStructId != 0)
            {
                var previousOffset = Reader.BaseStream.Position;

                var newStruct = Resource.IntrospectionManifest.ReferencedStructs.First(x => x.Id == refStruct.BaseStructId);

                // Valve doesn't print this struct's type, so we can't just call ReadStructure *sigh*
                foreach (var field in newStruct.FieldIntrospection)
                {
                    Reader.BaseStream.Position = startingOffset + field.OnDiskOffset;

                    ReadFieldIntrospection(field);
                }

                Reader.BaseStream.Position = previousOffset;
            }

            Writer.Indent--;
            Writer.WriteLine("}");
        }

        private void ReadFieldIntrospection(ResourceIntrospectionManifest.ResourceDiskStruct.Field field)
        {
            uint count = (uint)field.Count;
            bool multiple = false; // TODO: get rid of this
            bool pointer = false; // TODO: get rid of this

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
                        Writer.WriteLine("{0} {1}* = (ptr) ->NULL", ValveDataType(field.Type), field.FieldName);

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
                        multiple = true;

                        Reader.BaseStream.Position += offset - 8;
                    }
                }
                else
                {
                    throw new NotImplementedException(string.Format("Unknown indirection. ({0})", indirection));
                }
            }

            if (pointer)
            {
                Writer.Write("{0} {1}* = (ptr) ->", ValveDataType(field.Type), field.FieldName);
            }
            else if (field.Count > 0 || field.Indirections.Count > 0)
            {
                // TODO: This is matching Valve's incosistency
                if (field.Type == DataType.Byte && field.Indirections.Count > 0)
                {
                    Writer.WriteLine("{0}[{2}] {1} =", ValveDataType(field.Type), field.FieldName, count);
                }
                else
                {
                    Writer.WriteLine("{0} {1}[{2}] =", ValveDataType(field.Type), field.FieldName, count);
                }

                Writer.WriteLine("[");
                Writer.Indent++;
            }
            else
            {
                Writer.Write("{0} {1} = ", ValveDataType(field.Type), field.FieldName);
            }

            while (count-- > 0)
            {
                ReadField(field, multiple);
            }

            if (!pointer && (field.Count > 0 || field.Indirections.Count > 0))
            {
                Writer.Indent--;
                Writer.WriteLine("]");
            }

            if (prevOffset > 0)
            {
                Reader.BaseStream.Position = prevOffset;
            }
        }

        private void ReadField(ResourceIntrospectionManifest.ResourceDiskStruct.Field field, bool multiple)
        {
            switch (field.Type)
            {
                case DataType.Struct:
                    var newStruct = Resource.IntrospectionManifest.ReferencedStructs.First(x => x.Id == field.TypeData);

                    ReadStructure(newStruct, Reader.BaseStream.Position);

                    break;

                case DataType.Enum:
                    // TODO: Lookup in ReferencedEnums
                    Writer.WriteLine("{0}", Reader.ReadUInt32());
                    break;

                case DataType.SByte:
                    Writer.WriteLine("{0}", Reader.ReadSByte());
                    break;

                case DataType.Byte: // TODO: Valve print it as hex, why?
                    // TODO: if there are more than one uint8's, valve prints them without 0x, and on a single line
                    if (multiple)
                    {
                        Writer.WriteLine("{0:X2}", Reader.ReadByte());
                    }
                    else
                    {
                        Writer.WriteLine("0x{0:X2}", Reader.ReadByte());
                    }
                    break;

                case DataType.Boolean:
                    Writer.WriteLine("{0}", Reader.ReadByte() == 1 ? "true" : "false");
                    break;

                case DataType.Int16:
                    Writer.WriteLine("{0}", Reader.ReadInt16());
                    break;

                case DataType.UInt16: // TODO: Valve print it as hex, why?
                    Writer.WriteLine("0x{0:X4}", Reader.ReadUInt16());
                    break;

                case DataType.Int32:
                    Writer.WriteLine("{0}", Reader.ReadInt32());
                    break;

                case DataType.UInt32: // TODO: Valve print it as hex, why?
                    Writer.WriteLine("0x{0:X8}", Reader.ReadUInt32());
                    break;

                case DataType.Float:
                    Writer.WriteLine("{0:F6}", Reader.ReadSingle());
                    break;

                case DataType.Int64:
                    Writer.WriteLine("{0}", Reader.ReadInt64());
                    break;

                case DataType.UInt64: // TODO: Valve print it as hex, why?
                    Writer.WriteLine("0x{0:X16}", Reader.ReadUInt64());
                    break;

                case DataType.ExternalReference:
                    Writer.WriteLine("ID: {0:X16}", Reader.ReadUInt64());
                    break;

                case DataType.Vector:
                    var vector3 = new []
                    {
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle()
                    };

                    Writer.WriteLine("({0:F6}, {1:F6}, {2:F6})", vector3[0], vector3[1], vector3[2]);

                    break;

                case DataType.Quaternion:
                case DataType.Color:
                case DataType.Fltx4:
                case DataType.Vector4D:
                    var vector4 = new []
                    {
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle()
                    };

                    if (field.Type == DataType.Quaternion)
                    {
                        Writer.WriteLine("{{x: {0:F}, y: {1:F}, z: {2:F}, w: {3}}}", vector4[0], vector4[1], vector4[2], vector4[3].ToString("F"));
                    }
                    else
                    {
                        Writer.WriteLine("({0:F6}, {1:F6}, {2:F6}, {3:F6})", vector4[0], vector4[1], vector4[2], vector4[3]);
                    }

                    break;

                case DataType.String4:
                case DataType.String:
                    var offset = Reader.ReadUInt32();
                    var prev = Reader.BaseStream.Position;

                    Reader.BaseStream.Position += offset - 4;

                    Writer.WriteLine("\"{0}\"", Reader.ReadNullTermString(Encoding.UTF8));

                    Reader.BaseStream.Position = prev;
                    break;

                case DataType.Matrix3x4:
                case DataType.Matrix3x4a:
                    var matrix3x4a = new []
                    {
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),

                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),

                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle()
                    };
                    
                    Writer.WriteLine();
                    Writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", matrix3x4a[0], matrix3x4a[1], matrix3x4a[2], matrix3x4a[3]);
                    Writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", matrix3x4a[4], matrix3x4a[5], matrix3x4a[6], matrix3x4a[7]);
                    Writer.WriteLine("{0:F4} {1:F4} {2:F4} {3:F4}", matrix3x4a[8], matrix3x4a[9], matrix3x4a[10], matrix3x4a[11]);

                    break;

                case DataType.CTransform:
                    var transform = new []
                    {
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle(),
                        Reader.ReadSingle() // TODO: unused?
                    };

                    // http://stackoverflow.com/a/15085178/2200891
                    Writer.WriteLine("q={{{0:F}, {1:F}, {2:F}; w={3}}} p={{{4:F}, {5:F}, {6}}}", transform[4], transform[5], transform[6], transform[7].ToString("F"), transform[0], transform[1], transform[2].ToString("F"));

                    break;

                default:
                    throw new NotImplementedException(string.Format("Unknown data type: {0}", field.Type));
            }
        }

        public override string ToString()
        {
            return Output ?? "Nope.";
        }

        private static string ValveDataType(DataType type)
        {
            switch (type)
            {
                case DataType.SByte: return "int8";
                case DataType.Byte: return "uint8";
                case DataType.Int16: return "int16";
                case DataType.UInt16: return "uint16";
                case DataType.Int32: return "int32";
                case DataType.UInt32: return "uint32";
                case DataType.Int64: return "int64";
                case DataType.UInt64: return "uint64";
                case DataType.Float: return "float32";
                case DataType.String: return "CResourceString";
                case DataType.Boolean: return "bool";
                case DataType.Fltx4: return "fltx4";
                case DataType.Matrix3x4a: return "matrix3x4a_t";
            }

            return type.ToString();
        }
    }
}
