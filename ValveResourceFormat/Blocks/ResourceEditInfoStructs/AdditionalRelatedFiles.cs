using System;
using System.Collections.Generic;
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
        }

        public List<AdditionalRelatedFile> List;

        public AdditionalRelatedFiles()
        {
            List = new List<AdditionalRelatedFile>();
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new AdditionalRelatedFile();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.ContentRelativeFilename = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.ContentSearchPath = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                List.Add(dep);
            }
        }

        public override string ToString()
        {
            var str = new StringBuilder();

            str.AppendFormat("\tStruct m_AdditionalRelatedFiles[{0}]\n", List.Count);

            foreach (var dep in List)
            {
                str.AppendFormat(
                    "\t\tCResourceString m_ContentRelativeFilename = \"{0}\"\n" +
                    "\t\tCResourceString m_ContentSearchPath = \"{1}\"\n\n",
                    dep.ContentRelativeFilename, dep.ContentSearchPath
                );
            }

            return str.ToString();
        }
    }
}
