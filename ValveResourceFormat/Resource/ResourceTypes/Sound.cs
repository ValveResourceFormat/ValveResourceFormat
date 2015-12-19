using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public class Sound : Blocks.ResourceData
    {
        private BinaryReader Reader;
        private long DataOffset;
        private NTRO NTROBlock;

        public override void Read(BinaryReader reader, Resource resource)
        {
            Reader = reader;

            reader.BaseStream.Position = Offset;

            if (resource.Blocks.ContainsKey(BlockType.NTRO))
            {
                NTROBlock = new NTRO();
                NTROBlock.Offset = Offset;
                NTROBlock.Size = Size;
                NTROBlock.Read(reader, resource);
            }

            DataOffset = Offset + Size;
        }

        public byte[] GetSound()
        {
            Reader.BaseStream.Position = DataOffset;

            return Reader.ReadBytes((int)Reader.BaseStream.Length);
        }

        public override string ToString()
        {
            if (NTROBlock != null)
            {
                return NTROBlock.ToString();
            }

            return "This is a sound.";
        }
    }
}
