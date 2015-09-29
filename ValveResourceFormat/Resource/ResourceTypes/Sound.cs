using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public class Sound : Blocks.ResourceData
    {
        public byte[] SoundData { get; private set; }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;


            reader.BaseStream.Position = resource.FileSize;
            SoundData = reader.ReadBytes((int)reader.BaseStream.Length - (int)resource.FileSize);
        }

        public override string ToString()
        {
            return "This is a sound.";
        }
    }
}
