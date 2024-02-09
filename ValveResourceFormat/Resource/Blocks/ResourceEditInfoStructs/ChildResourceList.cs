using System.IO;
using System.Text;

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

        public List<ReferenceInfo> List { get; }

        public ChildResourceList()
        {
            List = new((int)Size);
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            for (var i = 0; i < Size; i++)
            {
                var dep = new ReferenceInfo
                {
                    Id = reader.ReadUInt64(),
                    ResourceName = reader.ReadOffsetString(Encoding.UTF8)
                };

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
