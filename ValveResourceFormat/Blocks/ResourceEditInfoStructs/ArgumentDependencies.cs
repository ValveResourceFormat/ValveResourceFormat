using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ArgumentDependencies : REDIBlock
    {
        public class ArgumentDependency
        {
            public string ParameterName { get; set; }
            public string ParameterType { get; set; }
            public uint Fingerprint { get; set; }
            public uint FingerprintDefault { get; set; }
        }

        public List<ArgumentDependency> List;

        public ArgumentDependencies()
        {
            List = new List<ArgumentDependency>();
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new ArgumentDependency();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.ParameterName = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.ParameterType = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                dep.Fingerprint = reader.ReadUInt32();
                dep.FingerprintDefault = reader.ReadUInt32();

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

            str.AppendFormat("{0}Struct m_ArgumentDependencies[{1}] =\n", indent, List.Count);
            str.AppendFormat("{0}[\n", indent);

            foreach (var dep in List)
            {
                str.AppendFormat("{0}\tResourceArgumentDependency_t\n", indent);
                str.AppendFormat("{0}\t{{\n", indent);
                str.AppendFormat("{0}\t\tCResourceString m_ParameterName = \"{1}\"\n", indent, dep.ParameterName);
                str.AppendFormat("{0}\t\tCResourceString m_ParameterType = \"{1}\"\n", indent, dep.ParameterType);
                str.AppendFormat("{0}\t\tuint32 m_nFingerprint = 0x{1:X8}\n", indent, dep.Fingerprint);
                str.AppendFormat("{0}\t\tuint32 m_nFingerprintDefault = 0x{1:X8}\n", indent, dep.FingerprintDefault);
                str.AppendFormat("{0}\t}}\n", indent);
            }

            str.AppendFormat("{0}]\n", indent);

            return str.ToString();
        }
    }
}
