using System.IO;
using System.IO.Hashing;
using System.Text;

#nullable disable

namespace ValveResourceFormat.ResourceTypes
{
    public class Panorama : Block
    {
        public class NameEntry
        {
            public string Name { get; set; }
            public uint Unknown1 { get; set; } // TODO: unconfirmed
            public uint Unknown2 { get; set; } // TODO: unconfirmed
        }

        public List<NameEntry> Names { get; } = [];

        public byte[] Data { get; private set; }
        public uint CRC32 { get; private set; }

        public override BlockType Type => BlockType.DATA;

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;

            if ((Resource.ResourceType == ResourceType.PanoramaScript && Resource.Version >= 4)
            || (Resource.ResourceType == ResourceType.PanoramaTypescript && Resource.Version >= 2))
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

        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.Write(Encoding.UTF8.GetString(Data));
        }
    }
}
