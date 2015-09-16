using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ExtraFloatData : REDIBlock
    {
        public class EditFloatData
        {
            public string Name { get; set; } 
            public float Float { get; set; }
        }

        public List<EditFloatData> List;

        public ExtraFloatData()
        {
            List = new List<EditFloatData>();
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new EditFloatData();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.Name = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                dep.Float = reader.ReadSingle();

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

            str.AppendFormat("{0}Struct m_ExtraFloatData[{1}] = \n", indent, List.Count);
            str.AppendFormat("{0}[\n", indent);

            foreach (var dep in List)
            {
                str.AppendFormat("{0}\tResourceEditFloatData_t\n", indent);
                str.AppendFormat("{0}\t{{\n", indent);
                str.AppendFormat("{0}\t\tCResourceString m_Name = \"{1}\"\n", indent, dep.Name);
                str.AppendFormat("{0}\t\tfloat32 m_flFloat = {1:F6}\n", indent, dep.Float);
                str.AppendFormat("{0}\t}}\n", indent);
            }

            str.AppendFormat("{0}]\n", indent);

            return str.ToString();
        }
    }
}
