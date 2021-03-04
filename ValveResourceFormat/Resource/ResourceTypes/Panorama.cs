using System.Collections.Generic;
using System.IO;
using System.Text;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class Panorama : ResourceData
    {
        public class NameEntry
        {
            public string Name { get; set; }
            public uint Unknown1 { get; set; } // TODO: unconfirmed
            public uint Unknown2 { get; set; } // TODO: unconfirmed
        }

        public List<NameEntry> Names { get; }

        public byte[] Data { get; private set; }
        public uint CRC32 { get; private set; }

        public Panorama()
        {
            Names = new List<NameEntry>();
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

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
            if (!resource.ContainsBlockType(BlockType.SrMa) && Crc32.Compute(Data) != CRC32)
            {
                throw new InvalidDataException("CRC32 mismatch for read data.");
            }
        }

        public override string ToString()
        {
            return Encoding.UTF8.GetString(Data);
        }
    }
}
