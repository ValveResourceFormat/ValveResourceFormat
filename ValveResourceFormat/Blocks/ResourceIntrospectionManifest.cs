using System;

using System.IO;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "NTRO" block. CResourceIntrospectionManifest
    /// </summary>
    public class ResourceIntrospectionManifest : Block
    {
        public override BlockType GetChar()
        {
            return BlockType.NTRO;
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;


        }
    }
}
