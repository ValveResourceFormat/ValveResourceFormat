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

        public override void Read(BinaryReader reader, Resource resource)
        {
            Reader = reader;
            Resource = resource;

            //reader.BaseStream.Position = this.Offset;

            using (var output = new StringWriter())
            using (var writer = new IndentedTextWriter(output, "\t"))
            {
                Writer = writer;

                foreach (var refStruct in resource.IntrospectionManifest.ReferencedStructs)
                {
                    ReadStructure(refStruct, this.Offset);

                    break;
                }

                Console.Write(output.ToString());
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

                /*if (field.Indirections.Count > 0)
                {
                    ReadIndirection(field);
                    continue;
                }*/

                switch (field.Type)
                {
                    case DataType.SubStructure:
                        var newStruct = Resource.IntrospectionManifest.ReferencedStructs.First(x => x.Id == field.TypeData);

                        if (field.Indirections.Count > 0)
                        {
                            Reader.ReadBytes(8);
                        }

                        ReadStructure(newStruct, Reader.BaseStream.Position + refStruct.DiskSize);

                        break;

                    case DataType.Byte:
                        Writer.WriteLine(Reader.ReadByte());
                        break;

                    case DataType.Boolean:
                        Writer.WriteLine(Reader.ReadByte());
                        break;

                    case DataType.Sint:
                        Writer.WriteLine(Reader.ReadUInt32());
                        break;

                    case DataType.Number:
                        Writer.WriteLine(Reader.ReadInt32());
                        break;

                    case DataType.Flags:
                        Writer.WriteLine(Reader.ReadInt32());
                        break;

                    case DataType.Float:
                        Writer.WriteLine(Reader.ReadSingle());
                        break;

                    case DataType.String4:
                    case DataType.String:
                        Reader.BaseStream.Position += Reader.ReadUInt32();

                        Writer.WriteLine(Reader.ReadNullTermString(Encoding.UTF8));
                        break;

                    default:
                        Writer.WriteLine("TODO: unknown datatype");
                        break;
                }
            }

            Writer.Indent--;
        }

        private void ReadIndirection(ResourceIntrospectionManifest.ResourceDiskStruct.Field field)
        {
            if (field.Indirections[0] == 0x04)
            {
                var offset = Reader.ReadUInt32();
                var size = Reader.ReadUInt32();

                Reader.BaseStream.Position += offset - 4;

                while (size-- > 0)
                {

                }

            }
        }

        public override string ToString()
        {
            return "yolo";
        }
    }
}
