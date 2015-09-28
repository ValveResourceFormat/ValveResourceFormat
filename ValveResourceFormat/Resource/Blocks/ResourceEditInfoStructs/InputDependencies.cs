using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class InputDependencies : REDIBlock
    {
        public class InputDependency
        {
            public string ContentRelativeFilename { get; set; }
            public string ContentSearchPath { get; set; }
            public uint FileCRC { get; set; }
            public uint Flags { get; set; }

            public void WriteText(IndentedTextWriter writer)
            {
                writer.WriteLine("ResourceInputDependency_t");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine("CResourceString m_ContentRelativeFilename = \"{0}\"", ContentRelativeFilename);
                writer.WriteLine("CResourceString m_ContentSearchPath = \"{0}\"", ContentSearchPath);
                writer.WriteLine("uint32 m_nFileCRC = 0x{0:X8}", FileCRC);
                writer.WriteLine("uint32 m_nFlags = 0x{0:X8}", Flags);
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        public List<InputDependency> List;

        public InputDependencies()
        {
            List = new List<InputDependency>();
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new InputDependency();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.ContentRelativeFilename = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.ContentSearchPath = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                dep.FileCRC = reader.ReadUInt32();
                dep.Flags = reader.ReadUInt32();

                List.Add(dep);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Struct m_InputDependencies[{0}] =", List.Count);
            WriteList(writer);
        }

        protected void WriteList(IndentedTextWriter writer)
        {
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
