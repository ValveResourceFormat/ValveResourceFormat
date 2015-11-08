using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.CodeDom.Compiler;

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

        public List<SpecialDependency> List;

        public SpecialDependencies()
        {
            List = new List<SpecialDependency>();
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new SpecialDependency();

                dep.String = reader.ReadOffsetString(Encoding.UTF8);
                dep.CompilerIdentifier = reader.ReadOffsetString(Encoding.UTF8);
                dep.Fingerprint = reader.ReadUInt32();
                dep.UserData = reader.ReadUInt32();

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
