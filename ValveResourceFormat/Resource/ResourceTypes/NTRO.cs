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

            Writer.Indent++;

            foreach (var field in refStruct.FieldIntrospection)
            {
                Reader.BaseStream.Position = startingOffset + field.OnDiskOffset;

                Writer.Write(field.FieldName + " " + field.Type + ": ");

                Writer.Indent++;

                ReadField(field);

                Writer.Indent--;
            }

            Writer.Indent--;
        }

        private void ReadField(ResourceIntrospectionManifest.ResourceDiskStruct.Field field)
        {
            uint count = 1;

            if (field.Indirections.Count > 0)
            {
                var offset = Reader.ReadUInt32();
                count = Reader.ReadUInt32();

                if (count == 0)
                {
                    Writer.WriteLine("empty");
                    return;
                }

                Reader.BaseStream.Position += offset - 8;
            }

            while (count-- > 0)
            {
                switch (field.Type)
                {
                    case DataType.SubStructure:
                        var newStruct = Resource.IntrospectionManifest.ReferencedStructs.First(x => x.Id == field.TypeData);

                        Writer.WriteLine();

                        ReadStructure(newStruct, Reader.BaseStream.Position);

                        break;

                    case DataType.Byte:
                        Writer.WriteLine("{0}", Reader.ReadByte());
                        break;

                    case DataType.Boolean:
                        Writer.WriteLine("{0}", Reader.ReadByte());
                        break;

                    case DataType.Sint:
                        Writer.WriteLine("{0}", Reader.ReadUInt32());
                        break;

                    case DataType.Number:
                        Writer.WriteLine("{0}", Reader.ReadInt32());
                        break;

                    case DataType.Flags:
                        Writer.WriteLine("{0}", Reader.ReadInt32());
                        break;

                    case DataType.Float:
                        Writer.WriteLine("{0:F6}", Reader.ReadSingle());
                        break;

                    case DataType.Quaternion:
                        Reader.ReadBytes(8); // TODO
                        break;

                    case DataType.Uint64:
                        Writer.WriteLine("{0}", Reader.ReadUInt64());
                        break;

                    case DataType.Extref:
                        Writer.WriteLine("{0}", Reader.ReadUInt64());
                        break;

                    case DataType.Vector3:
                        var vector = new []
                        {
                            Reader.ReadSingle(),
                            Reader.ReadSingle(),
                            Reader.ReadSingle()
                        };

                        Writer.WriteLine("[{0:F6}, {1:F6}, {2:F6}]", vector[0], vector[1], vector[2]);

                        break;

                    case DataType.String4:
                    case DataType.String:
                        var offset = Reader.ReadUInt32();
                        var prev = Reader.BaseStream.Position;

                        Reader.BaseStream.Position += offset - 4;

                        Writer.WriteLine(Reader.ReadNullTermString(Encoding.UTF8));

                        Reader.BaseStream.Position = prev;
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
    }
}
