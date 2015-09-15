using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

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
        }

        public List<InputDependency> List;

        public InputDependencies()
        {
            List = new List<InputDependency>();
        }

        public override void Read(BinaryReader reader)
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

        public override string ToString()
        {
            return ToStringIndent("");
        }

        public override string ToStringIndent(string indent)
        {
            var str = new StringBuilder();

            str.AppendFormat("{0}Struct m_InputDependencies[{1}] = \n", indent, List.Count);
            str.AppendFormat("{0}[\n", indent);

            foreach (var dep in List)
            {
                str.AppendFormat("{0}\tResourceInputDependency_t\n", indent);
                str.AppendFormat("{0}\t{{\n", indent);
                str.AppendFormat("{0}\t\tCResourceString m_ContentRelativeFilename = \"{1}\"\n", indent, dep.ContentRelativeFilename);
                str.AppendFormat("{0}\t\tCResourceString m_ContentSearchPath = \"{1}\"\n", indent, dep.ContentSearchPath);
                str.AppendFormat("{0}\t\tuint32 m_nFileCRC = 0x{1:X8}\n", indent, dep.FileCRC);
                str.AppendFormat("{0}\t\tuint32 m_nFlags = 0x{1:X8}\n", indent, dep.Flags);
                str.AppendFormat("{0}\t}}\n", indent);
            }

            str.AppendFormat("{0}]\n", indent);

            return str.ToString();
        }
    }
}
