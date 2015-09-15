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
            var str = new StringBuilder();

            str.AppendFormat("\tStruct m_ArgumentDependencies[{0}]\n", List.Count);

            foreach (var dep in List)
            {
                str.AppendFormat(
                    "\t\tCResourceString m_ParameterName = \"{0}\"\n" +
                    "\t\tCResourceString m_ParameterType = \"{1}\"\n" +
                    "\t\tuint32 m_nFingerprint = 0x{2:x8}\n" +
                    "\t\tuint32 m_nFingerprintDefault = 0x{3:x8}\n\n",
                    dep.ParameterName, dep.ParameterType, dep.Fingerprint, dep.FingerprintDefault
                );
            }

            return str.ToString();
        }
    }
}
