using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ResourceTypes
{
    public class Model : Blocks.ResourceData
    {
        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            var modelName = reader.ReadOffsetString(Encoding.UTF8);
        }
    }
}
