using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.CodeDom.Compiler;

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

        public List<AdditionalRelatedFile> List;

        public AdditionalRelatedFiles()
        {
            List = new List<AdditionalRelatedFile>();
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new AdditionalRelatedFile();

                dep.ContentRelativeFilename = reader.ReadOffsetString(Encoding.UTF8);
                dep.ContentSearchPath = reader.ReadOffsetString(Encoding.UTF8);

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
