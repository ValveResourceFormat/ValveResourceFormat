using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public class Panorama : Blocks.ResourceData
    {
        public byte[] Data { get; private set; }
        public uint Crc32 { get; private set; }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;

            Crc32 = reader.ReadUInt32();

            var size = reader.ReadUInt16();
            int headerSize = 4 + 2;

            for (var i = 0; i < size; i++)
            {
                var name = reader.ReadNullTermString(Encoding.UTF8);

                reader.ReadBytes(4); // TODO: ????

                headerSize += name.Length + 1 + 4; // string length + null byte + 4 bytes
            }

            Data = reader.ReadBytes((int)this.Size - headerSize);
            if (ValveResourceFormat.Crc32.Compute(Data) != Crc32)
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
