using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ExtraStringData : REDIBlock
    {
        public class EditStringData
        {
            public string Name { get; set; } 
            public string String { get; set; }
        }

        public List<EditStringData> List;

        public ExtraStringData()
        {
            List = new List<EditStringData>();
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new EditStringData();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.Name = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.String = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                List.Add(dep);
            }
        }

        public override string ToString()
        {
            return ToStringIndent("");
        }

        public override string ToStringIndent(string indent)
        {
            var str = new StringBuilder();

            str.AppendFormat("{0}Struct m_ExtraStringData[{1}] = \n", indent, List.Count);
            str.AppendFormat("{0}[\n", indent);

            foreach (var dep in List)
            {
                str.AppendFormat("{0}\tResourceEditStringData_t\n", indent);
                str.AppendFormat("{0}\t{{\n", indent);
                str.AppendFormat("{0}\t\tCResourceString m_Name = \"{1}\"\n", indent, dep.Name);
                str.AppendFormat("{0}\t\tCResourceString m_String = \"{1}\"\n", indent, dep.String);
                str.AppendFormat("{0}\t}}\n", indent);
            }

            str.AppendFormat("{0}]\n", indent);

            return str.ToString();
        }
    }
}
