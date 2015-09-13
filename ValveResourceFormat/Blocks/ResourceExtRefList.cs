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

            reader.ReadUInt32(); // always 8??

            var size = reader.ReadUInt32();

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
    }
}
