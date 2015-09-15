using System;
using System.Collections.Generic;
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
        }

        public List<ReferenceInfo> List;

        public ChildResourceList()
        {
            List = new List<ReferenceInfo>();
        }

        public override void Read(BinaryReader reader)
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

        public override string ToString()
        {
            return ToStringIndent("");
        }

        public override string ToStringIndent(string indent)
        {
            var str = new StringBuilder();

            str.AppendFormat("{0}Struct m_ChildResourceList[{1}] = \n", indent, List.Count);
            str.AppendFormat("{0}[\n", indent);

            foreach (var dep in List)
            {
                str.AppendFormat("{0}\tResourceReferenceInfo_t\n", indent);
                str.AppendFormat("{0}\t{{\n", indent);
                str.AppendFormat("{0}\t\tuint64 m_nId = 0x{1:X16}\n", indent, dep.Id);
                str.AppendFormat("{0}\t\tCResourceString m_pResourceName = \"{1}\"\n", indent, dep.ResourceName);
                str.AppendFormat("{0}\t}}\n", indent);
            }

            str.AppendFormat("{0}]\n", indent);

            return str.ToString();
        }
    }
}
