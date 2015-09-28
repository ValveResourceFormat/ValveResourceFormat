using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.CodeDom.Compiler;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ChildResourceList : REDIBlock
    {
        public class ReferenceInfo
        {
            public ulong Id { get; set; }
            public string ResourceName { get; set; }

            public void WriteText(IndentedTextWriter writer)
            {
                writer.WriteLine("ResourceReferenceInfo_t");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine("uint64 m_nId = 0x{0:X16}", Id);
                writer.WriteLine("CResourceString m_pResourceName = \"{0}\"", ResourceName);
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        public List<ReferenceInfo> List;

        public ChildResourceList()
        {
            List = new List<ReferenceInfo>();
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new ReferenceInfo();

                dep.Id = reader.ReadUInt64();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.ResourceName = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                reader.ReadBytes(4); // TODO: ????

                List.Add(dep);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Struct m_ChildResourceList[{0}] =", List.Count);
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
