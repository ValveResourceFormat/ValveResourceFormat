using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public class Panorama : Blocks.ResourceData
    {
        public byte[] Data { get; private set; }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            reader.ReadBytes(4); // TODO: ????

            var size = reader.ReadUInt16();
            int headerSize = 4 + 2;

            while (size-- > 0)
            {
                var name = reader.ReadNullTermString(Encoding.UTF8);

                reader.ReadBytes(4); // TODO: ????

                headerSize += name.Length + 1 + 4; // string length + null byte + 4 bytes
            }

            Data = reader.ReadBytes((int)this.Size - headerSize);
        }

        public override string ToString()
        {
            return Encoding.UTF8.GetString(Data);
        }
    }
}
