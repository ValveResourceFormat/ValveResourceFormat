using System;

namespace ValveResourceFormat.CompiledShader
{
    public class ZDataBlock : ShaderDataBlock
    {
        public int blockId { get; }
        public int h0 { get; }
        public int h1 { get; }
        public int h2 { get; }
        public byte[] dataload { get; }
        public ZDataBlock(ShaderDataReader datareader, int blockId) : base(datareader)
        {
            this.blockId = blockId;
            h0 = datareader.ReadInt32();
            h1 = datareader.ReadInt32();
            h2 = datareader.ReadInt32();
            if (h0 > 0)
            {
                dataload = datareader.ReadBytes(h0 * 4);
            }
        }
    }

    public abstract class GpuSource : ShaderDataBlock
    {
        public int sourceId { get; }
        public int offset { get; protected set; }
        public byte[] sourcebytes { get; protected set; } = Array.Empty<byte>();
        public byte[] editorRefId { get; protected set; }
        protected GpuSource(ShaderDataReader datareader, int sourceId) : base(datareader)
        {
            this.sourceId = sourceId;
        }
        public string GetEditorRefIdAsString()
        {
            var stringId = ShaderUtilHelpers.BytesToString(editorRefId);
            stringId = stringId.Replace(" ", "").ToLower();
            return stringId;
        }
        public abstract string GetBlockName();
    }

    public class GlslSource : GpuSource
    {
        public int arg0 { get; } // always 3
        // offset2, if present, always observes offset2 == offset + 8
        // offset2 can also be interpreted as the source-size
        public int offset2 { get; } = -1;
        public GlslSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            offset = datareader.ReadInt32();
            if (offset > 0)
            {
                arg0 = datareader.ReadInt32();
                offset2 = datareader.ReadInt32();
                sourcebytes = datareader.ReadBytes(offset2 - 1); // -1 because the sourcebytes are null-term
                datareader.BaseStream.Position += 1;
            }
            editorRefId = datareader.ReadBytes(16);
        }
        public override string GetBlockName()
        {
            return $"GLSL";
        }
    }

    public class DxilSource : GpuSource
    {
        public int arg0 { get; } // always 3
        public int arg1 { get; } // always 0xFFFF or 0xFFFE
        public int headerBytes { get; }
        public DxilSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            offset = datareader.ReadInt32();
            if (offset > 0)
            {
                arg0 = datareader.ReadInt16();
                arg1 = (int)datareader.ReadUInt16();
                uint dxilDelim = datareader.ReadUInt16();
                if (dxilDelim != 0xFFFE)
                {
                    throw new ShaderParserException($"Unexpected DXIL source id {dxilDelim:x08}");
                }

                headerBytes = (int)datareader.ReadUInt16() * 4; // size is given as a 4-byte count
                sourcebytes = datareader.ReadBytes(offset - 8); // size of source equals offset-8
            }
            editorRefId = datareader.ReadBytes(16);
        }
        public override string GetBlockName()
        {
            return $"DXIL";
        }
    }

    /*
     * The DXBC sources only have one header, the offset (which happens to be equal to their source size)
     */
    public class DxbcSource : GpuSource
    {
        public DxbcSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            this.offset = datareader.ReadInt32();
            if (offset > 0)
            {
                sourcebytes = datareader.ReadBytes(offset);
            }
            editorRefId = datareader.ReadBytes(16);
        }
        public override string GetBlockName()
        {
            return $"DXBC";
        }
    }

    public class VulkanSource : GpuSource
    {
        public int arg0 { get; } = -1;
        public int offset2 { get; } = -1;

        public VulkanSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            this.offset = datareader.ReadInt32();
            if (offset > 0)
            {
                this.arg0 = datareader.ReadInt32();
                this.offset2 = datareader.ReadInt32();
                sourcebytes = datareader.ReadBytes(offset - 8);
            }
            editorRefId = datareader.ReadBytes(16);
        }
        public override string GetBlockName()
        {
            return $"VULKAN";
        }
    }

}
