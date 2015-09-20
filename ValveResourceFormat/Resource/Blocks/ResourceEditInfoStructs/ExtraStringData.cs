using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ExtraStringData : REDIBlock
    {
        public class EditStringData
        {
            public string Name { get; set; } 
            public string Value { get; set; }

            public void WriteText(IndentedTextWriter writer)
            {
                writer.WriteLine("ResourceEditStringData_t");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine("CResourceString m_Name = \"{0}\"", Name);

                var lines = Value.Split(new [] { "\r\n", "\n" }, StringSplitOptions.None);

                if (lines.Length > 1)
                {
                    writer.Indent++;

                    writer.Write("CResourceString m_String = \"");

                    foreach (var line in lines)
                    {
                        writer.WriteLine(line);
                    }

                    writer.WriteLine("\"");

                    writer.Indent--;
                }
                else
                {
                    writer.WriteLine("CResourceString m_String = \"{0}\"", Value);
                }

                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        public List<EditStringData> List;

        public ExtraStringData()
        {
            List = new List<EditStringData>();
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new EditStringData();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.Name = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.Value = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                List.Add(dep);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Struct m_ExtraStringData[{0}] = ", List.Count);
            writer.WriteLine("[");
            writer.Indent++;

            foreach (var dep in List)
            {
                dep.WriteText(writer);
            }

            writer.Indent--;
            writer.WriteLine("]");
        }
    }
}
