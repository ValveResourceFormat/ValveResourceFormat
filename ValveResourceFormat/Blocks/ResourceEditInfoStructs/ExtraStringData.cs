using System;
using System.IO;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ExtraStringData : REDIBlock
    {
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;


        }
    }
}
