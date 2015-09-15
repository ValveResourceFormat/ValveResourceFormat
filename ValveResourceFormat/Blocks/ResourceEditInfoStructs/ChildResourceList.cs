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
            var str = new StringBuilder();

            str.AppendFormat("\tStruct m_ChildResourceList[{0}]\n", List.Count);

            foreach (var dep in List)
            {
                str.AppendFormat(
                    "\t\tuint64 m_nId = 0x{0:X8}\n" +
                    "\t\tCResourceString m_pResourceName = \"{1}\"\n\n",
                    dep.Id, dep.ResourceName
                );
            }

            return str.ToString();
        }
    }
}
