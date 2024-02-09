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

            public void WriteText(IndentedTextWriter writer)
            {
                writer.WriteLine("ResourceArgumentDependency_t");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine("CResourceString m_ParameterName = \"{0}\"", ParameterName);
                writer.WriteLine("CResourceString m_ParameterType = \"{0}\"", ParameterType);
                writer.WriteLine("uint32 m_nFingerprint = 0x{0:X8}", Fingerprint);
                writer.WriteLine("uint32 m_nFingerprintDefault = 0x{0:X8}", FingerprintDefault);
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        public List<ArgumentDependency> List { get; }

        public ArgumentDependencies()
        {
            List = new((int)Size);
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            for (var i = 0; i < Size; i++)
            {
                var dep = new ArgumentDependency
                {
                    ParameterName = reader.ReadOffsetString(Encoding.UTF8),
                    ParameterType = reader.ReadOffsetString(Encoding.UTF8),
                    Fingerprint = reader.ReadUInt32(),
                    FingerprintDefault = reader.ReadUInt32()
                };

                List.Add(dep);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Struct m_ArgumentDependencies[{0}] =", List.Count);
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
