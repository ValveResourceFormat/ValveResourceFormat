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

                if (field.Indirections.Count > 0)
                {
                    Writer.WriteLine("{0} {1}[{2}] =", ValveDataType(field.Type), field.FieldName, field.Indirections.Count); // TODO: printing count like this is incorrect
                    Writer.WriteLine("[");
                    Writer.Indent++;
                }
                else
                {
                    Writer.Write("{0} {1} = ", ValveDataType(field.Type), field.FieldName);
                }

                ReadField(field, field.Indirections.Count);

                if (field.Indirections.Count > 0)
                {
                    Writer.Indent--;
                    Writer.WriteLine("]");
                }
            }

            Writer.Indent--;
            Writer.WriteLine("}");
        }

        private void ReadField(ResourceIntrospectionManifest.ResourceDiskStruct.Field field, int indirectionDepth)
        {
            uint count = 1;

            if (indirectionDepth > 0)
            {
                var indirection = field.Indirections[field.Indirections.Count - indirectionDepth];

                var offset = Reader.ReadUInt32();

                if (indirection == 0x03)
                {
                    if (offset == 0)
                    {
                        return;
                    }

                    Reader.BaseStream.Position += offset - 4;
                }
                else if (indirection == 0x04)
                {
                    count = Reader.ReadUInt32();

                    if (count == 0)
                    {
                        return;
                    }

                    Reader.BaseStream.Position += offset - 8;
                }
                else
                {
                    throw new NotImplementedException(string.Format("Unknown indirection. ({0})", indirection));
                }
            }

            while (count-- > 0)
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

                    case DataType.Byte: // TODO: Valve print it as hex, why?
                        // TODO: if there are more than one uint8's, valve prints them without 0x, and on a single line
                        Writer.WriteLine("0x{0:X2}", Reader.ReadByte());
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
                    case DataType.Vector4:
                        var vector4 = new []
                        {
                            Reader.ReadSingle(),
                            Reader.ReadSingle(),
                            Reader.ReadSingle(),
                            Reader.ReadSingle()
                        };

                        Writer.WriteLine("[{0:F6}, {1:F6}, {2:F6}, {3:F6}]", vector4[0], vector4[1], vector4[2], vector4[3]);

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

                        Writer.WriteLine("[{0:F6}, {1:F6}, {2:F6}, {3:F6}]", matrix3x4a[0], matrix3x4a[1], matrix3x4a[2], matrix3x4a[3]);
                        Writer.WriteLine("[{0:F6}, {1:F6}, {2:F6}, {3:F6}]", matrix3x4a[4], matrix3x4a[5], matrix3x4a[6], matrix3x4a[7]);
                        Writer.WriteLine("[{0:F6}, {1:F6}, {2:F6}, {3:F6}]", matrix3x4a[8], matrix3x4a[9], matrix3x4a[10], matrix3x4a[11]);

                        break;

                    case DataType.CTransform:
                        Reader.ReadBytes(32);

                        Writer.WriteLine("yes this is a CTransform, fix me");

                        break;

                    default:
                        throw new NotImplementedException(string.Format("Unknown data type: {0}", field.Type));
                }
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
