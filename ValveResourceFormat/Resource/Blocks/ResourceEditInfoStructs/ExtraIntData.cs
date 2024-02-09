using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ExtraIntData : REDIBlock
    {
        public class EditIntData
        {
            public string Name { get; set; }
            public int Value { get; set; }

            public void WriteText(IndentedTextWriter writer)
            {
                writer.WriteLine("ResourceEditIntData_t");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine("CResourceString m_Name = \"{0}\"", Name);
                writer.WriteLine("int32 m_nInt = {0}", Value);
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        public List<EditIntData> List { get; }

        public ExtraIntData()
        {
            List = new((int)Size);
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            for (var i = 0; i < Size; i++)
            {
                var dep = new EditIntData
                {
                    Name = reader.ReadOffsetString(Encoding.UTF8),
                    Value = reader.ReadInt32()
                };

                List.Add(dep);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Struct m_ExtraIntData[{0}] =", List.Count);
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
