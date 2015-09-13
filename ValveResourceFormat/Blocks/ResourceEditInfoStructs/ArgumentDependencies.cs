using System;
using System.IO;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public class ArgumentDependencies : REDIBlock
    {
        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;


        }
    }
}
