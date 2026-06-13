using System.IO;
using System.Linq;
using System.Text;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "RERL" block. ResourceExtRefList_t.
    /// </summary>
    public class ResourceExtRefList : Block
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.RERL;

        /// <summary>
        /// Represents an external resource reference.
        /// </summary>
        public class ResourceReferenceInfo
        {
            /// <summary>
            /// Gets or sets the resource id.
            /// </summary>
            public ulong Id { get; set; }

            /// <summary>
            /// Gets or sets the resource name.
            /// </summary>
            public required string Name { get; set; }

            /// <summary>
            /// Writes the resource reference info as text.
            /// </summary>
            public void WriteText(IndentedTextWriter writer)
            {
                writer.WriteLine("ResourceReferenceInfo_t");
                writer.WriteLine("{");
                writer.Indent++;
                writer.WriteLine("uint64 m_nId = 0x{0:X16}", Id);
                writer.WriteLine("CResourceString m_pResourceName = \"{0}\"", Name);
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        /// <summary>
        /// Gets the list of external resource references.
        /// </summary>
        public List<ResourceReferenceInfo> ResourceRefInfoList { get; private set; }

        /// <summary>
        /// Gets the resource name mapped to the specified identifier, or <c>null</c> if missing.
        /// </summary>
        public string? this[ulong id]
        {
            get
            {
                var value = ResourceRefInfoList.FirstOrDefault(c => c.Id == id);

                return value?.Name;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceExtRefList"/> class.
        /// </summary>
        public ResourceExtRefList()
        {
            ResourceRefInfoList = [];
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            var offset = reader.ReadUInt32();
            var size = reader.ReadUInt32();

            if (size == 0)
            {
                return;
            }

            reader.BaseStream.Position += offset - 8; // 8 is 2 uint32s we just read

            ResourceRefInfoList.EnsureCapacity((int)size);

            for (var i = 0; i < size; i++)
            {
                var id = reader.ReadUInt64();
                var previousPosition = reader.BaseStream.Position;

                // jump to string
                // offset is counted from current position,
                // so we will need to add 8 to position later
                reader.BaseStream.Position += reader.ReadInt64();

                var name = reader.ReadNullTermString(Encoding.UTF8);

                reader.BaseStream.Position = previousPosition + 8; // 8 is to account for string offset

                ResourceRefInfoList.Add(new ResourceReferenceInfo
                {
                    Id = id,
                    Name = name,
                });
            }
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            if (ResourceRefInfoList.Count == 0)
            {
                writer.Write(0u);
                writer.Write(0u);
                return;
            }

            writer.Write(8u); // size of the 2 ints we are writing right now
            writer.Write(ResourceRefInfoList.Count);

            const uint EntrySize = sizeof(ulong) + sizeof(long);
            var stringsStartOffset = ResourceRefInfoList.Count * EntrySize;
            var currentStringOffset = 0;

            for (var i = 0; i < ResourceRefInfoList.Count; i++)
            {
                var refInfo = ResourceRefInfoList[i];

                writer.Write(refInfo.Id);

                var currentPosAfterID = sizeof(ulong) + i * EntrySize;
                var stringAbsolutePos = stringsStartOffset + currentStringOffset;
                var relativeOffset = stringAbsolutePos - currentPosAfterID;

                writer.Write(relativeOffset);

                currentStringOffset += Encoding.UTF8.GetByteCount(refInfo.Name) + 1;
            }

            foreach (var refInfo in ResourceRefInfoList)
            {
                var nameBytes = Encoding.UTF8.GetBytes(refInfo.Name);
                writer.Write(nameBytes);
                writer.Write((byte)0);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Outputs the external reference list in a structured format showing resource IDs and names.
        /// </remarks>
        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("ResourceExtRefList_t");
            writer.WriteLine("{");
            writer.Indent++;
            writer.WriteLine("Struct m_resourceRefInfoList[{0}] =", ResourceRefInfoList.Count);
            writer.WriteLine("[");
            writer.Indent++;

            foreach (var refInfo in ResourceRefInfoList)
            {
                refInfo.WriteText(writer);
            }

            writer.Indent--;
            writer.WriteLine("]");
            writer.Indent--;
            writer.WriteLine("}");
        }
    }
}
