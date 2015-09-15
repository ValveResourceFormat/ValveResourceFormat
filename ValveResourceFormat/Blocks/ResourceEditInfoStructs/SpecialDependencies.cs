using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class SpecialDependencies : REDIBlock
    {
        public class SpecialDependency
        {
            public string String { get; set; }
            public string CompilerIdentifier { get; set; }
            public uint Fingerprint { get; set; }
            public uint UserData { get; set; }
        }

        public List<SpecialDependency> List;

        public SpecialDependencies()
        {
            List = new List<SpecialDependency>();
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new SpecialDependency();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.String = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.CompilerIdentifier = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                dep.Fingerprint = reader.ReadUInt32();
                dep.UserData = reader.ReadUInt32();

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

            str.AppendFormat("{0}Struct m_SpecialDependencies[{1}] =\n", indent, List.Count);
            str.AppendFormat("{0}[\n", indent);

            foreach (var dep in List)
            {
                str.AppendFormat("{0}\tResourceSpecialDependency_t\n", indent);
                str.AppendFormat("{0}\t{{\n", indent);
                str.AppendFormat("{0}\t\tCResourceString m_String = \"{1}\"\n", indent, dep.String);
                str.AppendFormat("{0}\t\tCResourceString m_CompilerIdentifier = \"{1}\"\n", indent, dep.CompilerIdentifier);
                str.AppendFormat("{0}\t\tuint32 m_nFingerprint = 0x{1:X8}\n", indent, dep.Fingerprint);
                str.AppendFormat("{0}\t\tuint32 m_nUserData = 0x{1:X8}\n", indent, dep.UserData);
                str.AppendFormat("{0}\t}}\n", indent);
            }

            str.AppendFormat("{0}]\n", indent);

            return str.ToString();
        }
    }
}
