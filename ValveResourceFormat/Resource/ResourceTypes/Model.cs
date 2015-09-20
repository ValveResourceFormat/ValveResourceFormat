using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public class Model : Blocks.ResourceData
    {
        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = this.Offset;

            // Jump to model name offset
            reader.BaseStream.Position += reader.ReadUInt32();
            var modelName = reader.ReadNullTermString(Encoding.UTF8);

            // Return back and account for offset
            reader.BaseStream.Position = this.Offset + 4;
        }
    }
}
