using System;
using System.IO;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class ResponseRules : ResourceData
    {
        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return "";
        }
    }
}
