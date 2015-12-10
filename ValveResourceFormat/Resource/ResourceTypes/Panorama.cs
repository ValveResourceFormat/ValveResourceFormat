using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public class Panorama : Blocks.ResourceData
    {
        public class NameEntry
        {
            public string Name { get; set; }
            public uint CRC32 { get; set; } // TODO: unconfirmed
        }

        public List<NameEntry> Names;

        public byte[] Data { get; private set; }
        public uint CRC32 { get; private set; }

        public Panorama()
        {
            Names = new List<NameEntry>();
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;

            CRC32 = reader.ReadUInt32();

            var size = reader.ReadUInt16();
            int headerSize = 4 + 2;

            for (var i = 0; i < size; i++)
            {
                var entry = new NameEntry
                {
                    Name = reader.ReadNullTermString(Encoding.UTF8),
                    CRC32 = reader.ReadUInt32(),
                };

                Names.Add(entry);

                headerSize += entry.Name.Length + 1 + 4; // string length + null byte + 4 bytes
            }

            Data = reader.ReadBytes((int)this.Size - headerSize);

            if (Crc32.Compute(Data) != CRC32)
            {
                throw new InvalidDataException("CRC32 mismatch for read data.");
            }
        }

        public override string ToString()
        {
            return Encoding.UTF8.GetString(Data).Replace("\n", Environment.NewLine); //make sure panorama is new lines
        }
    }
}
