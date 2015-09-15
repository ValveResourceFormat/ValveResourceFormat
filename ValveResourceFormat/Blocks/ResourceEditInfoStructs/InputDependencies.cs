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
            var str = new StringBuilder();

            str.AppendFormat("\tStruct m_InputDependencies[{0}]\n", List.Count);

            foreach (var dep in List)
            {
                str.AppendFormat(
                    "\t\tCResourceString m_ContentRelativeFilename = \"{0}\"\n" +
                    "\t\tCResourceString m_ContentSearchPath = \"{1}\"\n" +
                    "\t\tint32 m_nFileCRC = 0x{2:x8}\n" +
                    "\t\tint32 m_nFlags = 0x{3:x8}\n\n",
                    dep.ContentRelativeFilename, dep.ContentSearchPath, dep.FileCRC, dep.Flags
                );
            }

            return str.ToString();
        }
    }
}
