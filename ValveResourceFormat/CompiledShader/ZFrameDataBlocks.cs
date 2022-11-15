using System;

namespace ValveResourceFormat.CompiledShader
{
    public class ZDataBlock : ShaderDataBlock
    {
        public int BlockId { get; }
        public int H0 { get; }
        public int H1 { get; }
        public int H2 { get; }
        public byte[] Dataload { get; }
        public ZDataBlock(ShaderDataReader datareader, int blockId) : base(datareader)
        {
            BlockId = blockId;
            H0 = datareader.ReadInt32();
            H1 = datareader.ReadInt32();
            H2 = datareader.ReadInt32();
            if (H0 > 0)
            {
                Dataload = datareader.ReadBytes(H0 * 4);
            }
        }
    }

    public abstract class GpuSource : ShaderDataBlock
    {
        public int SourceId { get; }
        public int Offset { get; protected set; }
        public byte[] Sourcebytes { get; protected set; } = Array.Empty<byte>();
        public byte[] EditorRefId { get; protected set; }
        protected GpuSource(ShaderDataReader datareader, int sourceId) : base(datareader)
        {
            SourceId = sourceId;
        }
        public string GetEditorRefIdAsString()
        {
            var stringId = ShaderUtilHelpers.BytesToString(EditorRefId);
            stringId = stringId.Replace(" ", "").ToLower();
            return stringId;
        }
        public abstract string GetBlockName();
    }

    public class GlslSource : GpuSource
    {
        public int Arg0 { get; } // always 3
        // offset2, if present, always observes offset2 == offset + 8
        // offset2 can also be interpreted as the source-size
        public int Offset2 { get; } = -1;
        public GlslSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            Offset = datareader.ReadInt32();
            if (Offset > 0)
            {
                Arg0 = datareader.ReadInt32();
                Offset2 = datareader.ReadInt32();
                Sourcebytes = datareader.ReadBytes(Offset2 - 1); // -1 because the sourcebytes are null-term
                datareader.BaseStream.Position += 1;
            }
            EditorRefId = datareader.ReadBytes(16);
        }
        public override string GetBlockName()
        {
            return $"GLSL";
        }
    }

    public class DxilSource : GpuSource
    {
        public int Arg0 { get; } // always 3
        public int Arg1 { get; } // always 0xFFFF or 0xFFFE
        public int HeaderBytes { get; }
        public DxilSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            Offset = datareader.ReadInt32();
            if (Offset > 0)
            {
                Arg0 = datareader.ReadInt16();
                Arg1 = (int)datareader.ReadUInt16();
                uint dxilDelim = datareader.ReadUInt16();
                if (dxilDelim != 0xFFFE)
                {
                    throw new ShaderParserException($"Unexpected DXIL source id {dxilDelim:x08}");
                }

                HeaderBytes = (int)datareader.ReadUInt16() * 4; // size is given as a 4-byte count
                Sourcebytes = datareader.ReadBytes(Offset - 8); // size of source equals offset-8
            }
            EditorRefId = datareader.ReadBytes(16);
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
            Offset = datareader.ReadInt32();
            if (Offset > 0)
            {
                Sourcebytes = datareader.ReadBytes(Offset);
            }
            EditorRefId = datareader.ReadBytes(16);
        }
        public override string GetBlockName()
        {
            return $"DXBC";
        }
    }

    public class VulkanSource : GpuSource
    {
        public int Arg0 { get; } = -1;
        public int Offset2 { get; } = -1;

        public VulkanSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            Offset = datareader.ReadInt32();
            if (Offset > 0)
            {
                Arg0 = datareader.ReadInt32();
                Offset2 = datareader.ReadInt32();
                Sourcebytes = datareader.ReadBytes(Offset - 8);
            }
            EditorRefId = datareader.ReadBytes(16);
        }
        public override string GetBlockName()
        {
            return $"VULKAN";
        }
    }

}
