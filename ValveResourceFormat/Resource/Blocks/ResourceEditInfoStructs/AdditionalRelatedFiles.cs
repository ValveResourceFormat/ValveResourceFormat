using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class AdditionalRelatedFiles : REDIBlock
    {
        public class AdditionalRelatedFile
        {
            public string ContentRelativeFilename { get; set; }
            public string ContentSearchPath { get; set; }

            public void WriteText(IndentedTextWriter writer)
            {
                writer.WriteLine("ResourceAdditionalRelatedFile_t");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine("CResourceString m_ContentRelativeFilename = \"{0}\"", ContentRelativeFilename);
                writer.WriteLine("CResourceString m_ContentSearchPath = \"{0}\"", ContentSearchPath);
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        public List<AdditionalRelatedFile> List { get; }

        public AdditionalRelatedFiles()
        {
            List = new((int)Size);
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            for (var i = 0; i < Size; i++)
            {
                var dep = new AdditionalRelatedFile
                {
                    ContentRelativeFilename = reader.ReadOffsetString(Encoding.UTF8),
                    ContentSearchPath = reader.ReadOffsetString(Encoding.UTF8)
                };

                List.Add(dep);
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Struct m_AdditionalRelatedFiles[{0}] =", List.Count);
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
