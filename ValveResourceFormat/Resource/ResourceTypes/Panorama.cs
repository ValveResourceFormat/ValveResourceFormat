using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Represents a Panorama UI resource.
    /// </summary>
    public class Panorama : Block
    {
        /// <summary>
        /// Represents a name entry.
        /// </summary>
        public class NameEntry
        {
            /// <summary>
            /// Gets or sets the name.
            /// </summary>
            public required string Name { get; set; }
            /// <summary>
            /// Gets or sets the first unknown value.
            /// </summary>
            public uint Unknown1 { get; set; } // TODO: unconfirmed
            /// <summary>
            /// Gets or sets the second unknown value.
            /// </summary>
            public uint Unknown2 { get; set; } // TODO: unconfirmed
        }

        /// <summary>
        /// Gets the list of name entries.
        /// </summary>
        public List<NameEntry> Names { get; } = [];

        /// <summary>
        /// Gets the raw data.
        /// </summary>
        public byte[] Data { get; private set; } = [];
        /// <summary>
        /// Gets the CRC32 checksum.
        /// </summary>
        public uint CRC32 { get; private set; }

        /// <inheritdoc/>
        public override BlockType Type => BlockType.DATA;

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            Debug.Assert(Resource != null);

            if (IsPlaintext())
            {
                Data = reader.ReadBytes((int)Size);

                return;
            }

            CRC32 = reader.ReadUInt32();

            var size = reader.ReadUInt16();

            for (var i = 0; i < size; i++)
            {
                var entry = new NameEntry
                {
                    Name = reader.ReadNullTermString(Encoding.UTF8),
                    Unknown1 = reader.ReadUInt32(), // TODO: This might be uint64 and be m_nId, same as RERL
                    Unknown2 = reader.ReadUInt32(),
                };

                Names.Add(entry);
            }

            var headerSize = reader.BaseStream.Position - Offset;

            Data = reader.ReadBytes((int)Size - (int)headerSize);

            // Valve seemingly screwed up when they started minifying vcss and the crc no longer matches
            // See core/pak01 in Artifact Foundry for such files
            if (Data.Length > 0 && !Resource.ContainsBlockType(BlockType.SrMa) && Crc32.HashToUInt32(Data) != CRC32)
            {
                throw new InvalidDataException("CRC32 mismatch for read data.");
            }
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            if (IsPlaintext())
            {
                stream.Write(Data);
                return;
            }

            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Outputs the Panorama data as UTF-8 encoded text.
        /// </remarks>
        public override void WriteText(IndentedTextWriter writer)
        {
            writer.Write(Encoding.UTF8.GetString(Data));
        }

        private bool IsPlaintext()
        {
            Debug.Assert(Resource != null);

            if (Resource.ResourceType == ResourceType.PanoramaScript && Resource.Version >= 4)
            {
                return true;
            }

            if (Resource.ResourceType == ResourceType.PanoramaTypescript && Resource.Version >= 2)
            {
                return true;
            }

            return false;
        }
    }
}
