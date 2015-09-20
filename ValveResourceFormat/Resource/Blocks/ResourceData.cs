using System;
using System.CodeDom.Compiler;
using System.IO;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// Resource data.
    /// </summary>
    public class ResourceData : Block
    {
        public override BlockType GetChar()
        {
            return BlockType.DATA;
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            throw new NotImplementedException("WriteText() in ResourceData");
        }
    }
}
