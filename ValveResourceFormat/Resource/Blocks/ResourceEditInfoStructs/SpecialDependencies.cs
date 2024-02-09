using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class SpecialDependencies : REDIBlock
    {
        public class SpecialDependency
        {
            public string String { get; set; }
            public string CompilerIdentifier { get; set; }
            public long Fingerprint { get; set; }
            public long UserData { get; set; }

            public void WriteText(IndentedTextWriter writer)
            {
                writer.WriteLine("ResourceSpecialDependency_t");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine("CResourceString m_String = \"{0}\"", String);
                writer.WriteLine("CResourceString m_CompilerIdentifier = \"{0}\"", CompilerIdentifier);
                writer.WriteLine("uint32 m_nFingerprint = 0x{0:X8}", Fingerprint);
                writer.WriteLine("uint32 m_nUserData = 0x{0:X8}", UserData);
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        public List<SpecialDependency> List { get; }

        public SpecialDependencies()
        {
            List = new((int)Size);
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            for (var i = 0; i < Size; i++)
            {
                var dep = new SpecialDependency
                {
                    String = reader.ReadOffsetString(Encoding.UTF8),
                    CompilerIdentifier = reader.ReadOffsetString(Encoding.UTF8),
                    Fingerprint = reader.ReadUInt32(),
                    UserData = reader.ReadUInt32()
                };

                List.Add(dep);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Struct m_SpecialDependencies[{0}] =", List.Count);
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
