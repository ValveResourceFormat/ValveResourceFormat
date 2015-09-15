using System;

namespace ValveResourceFormat.Blocks.ResourceEditInfoStructs
{
    public abstract class REDIBlock : Block
    {
        public override BlockType GetChar()
        {
            return BlockType.REDI;
        }

        public abstract string ToStringIndent(string indent);
    }
}
