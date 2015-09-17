using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "RERL" block. ResourceExtRefList_t
    /// </summary>
    public class ResourceExtRefList : Block
    {
        public class ResourceReferenceInfo
        {
            /// <summary>
            /// Resource id.
            /// </summary>
            public ulong Id { get; set; }

            /// <summary>
            /// Resource name.
            /// </summary>
            public string Name { get; set; }
        }

        public readonly List<ResourceReferenceInfo> ResourceRefInfoList;

        public ResourceExtRefList()
        {
            ResourceRefInfoList = new List<ResourceReferenceInfo>();
        }

        public override BlockType GetChar()
        {
            return BlockType.RERL;
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            var offset = reader.ReadUInt32();
            var size = reader.ReadUInt32();

            reader.BaseStream.Position += offset - 8; // 8 is 2 uint32s we just read

            while (size-- > 0)
            {
                var resInfo = new ResourceReferenceInfo();
                resInfo.Id = reader.ReadUInt64();

                var previousPosition = reader.BaseStream.Position;

                // jump to string
                // offset is counted from current position,
                // so we will need to add 8 to position later
                reader.BaseStream.Position += reader.ReadInt64();

                resInfo.Name = reader.ReadNullTermString(Encoding.UTF8);

                ResourceRefInfoList.Add(resInfo);

                reader.BaseStream.Position = previousPosition + 8; // 8 is to account for string offset
            }
        }

        public override string ToString()
        {
            var str = new StringBuilder();

            str.AppendLine("ResourceExtRefList_t");
            str.AppendLine("\t{");

            str.AppendFormat("\t\tStruct m_resourceRefInfoList[{0}] = \n", ResourceRefInfoList.Count);
            str.AppendLine("\t\t[");

            foreach (var dep in ResourceRefInfoList)
            {
                str.AppendLine("\t\t\tResourceReferenceInfo_t");
                str.AppendLine("\t\t\t{");
                str.AppendFormat(
                    "\t\t\t\tuint64 m_nId = 0x{0:X16}\n" +
                    "\t\t\t\tCResourceString m_pResourceName = \"{1}\"\n",
                    dep.Id, dep.Name
                );
                str.AppendLine("\t\t\t}");
            }

            str.AppendLine("\t\t]");
            str.AppendLine("\t}");

            return str.ToString();
        }
    }
}
