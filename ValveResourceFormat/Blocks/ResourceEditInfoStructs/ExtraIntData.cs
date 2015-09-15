using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ExtraIntData : REDIBlock
    {
        public class EditIntData
        {
            public string Name { get; set; } 
            public int Int { get; set; }
        }

        public List<EditIntData> List;

        public ExtraIntData()
        {
            List = new List<EditIntData>();
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            for (var i = 0; i < this.Size; i++)
            {
                var dep = new EditIntData();

                var prev = reader.BaseStream.Position;
                reader.BaseStream.Position += reader.ReadUInt32();
                dep.Name = reader.ReadNullTermString(Encoding.UTF8);
                reader.BaseStream.Position = prev + 4;

                dep.Int = reader.ReadInt32();

                List.Add(dep);
            }
        }

        public override string ToString()
        {
            var str = new StringBuilder();

            str.AppendFormat("\tStruct m_ExtraIntData[{0}]\n", List.Count);

            foreach (var dep in List)
            {
                str.AppendFormat(
                    "\t\tCResourceString m_Name = \"{0}\"\n" +
                    "\t\tint32 m_nInt = {1}\n\n",
                    dep.Name, dep.Int
                );
            }

            return str.ToString();
        }
    }
}
