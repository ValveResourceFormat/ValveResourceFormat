using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader
{
    public class ZDataBlock : ShaderDataBlock
    {
        public int BlockId { get; }
        public int H0 { get; }
        public int H1 { get; }
        public int H2 { get; }

        public WriteSeqField[] Fields { get; }
        public IReadOnlyList<WriteSeqField> Evaluated => Fields[..H1];
        public IReadOnlyList<WriteSeqField> Segment1 => Fields[H1..H2];
        public IReadOnlyList<WriteSeqField> Globals => Fields[H2..];
        public ReadOnlySpan<byte> Dataload => MemoryMarshal.AsBytes<WriteSeqField>(Fields);

        public ZDataBlock(ShaderDataReader datareader, int blockId) : base(datareader)
        {
            BlockId = blockId;
            H0 = datareader.ReadInt32();
            H1 = datareader.ReadInt32();
            H2 = datareader.ReadInt32();

            Fields = new WriteSeqField[H0];
            for (var i = 0; i < H0; i++)
            {
                Fields[i] = MemoryMarshal.AsRef<WriteSeqField>(datareader.ReadBytes(4));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
    public readonly struct WriteSeqField
    {
        private readonly byte paramId;
        public WriteSeqFieldFlags UnknFlags { get; }
        public byte Dest { get; }
        public byte Control { get; }

        public int ParamId => UnknFlags.HasFlag(WriteSeqFieldFlags.ExtraParam) ? paramId | 0x100 : paramId;
    }

    [Flags]
#pragma warning disable CA1028 // Enum storage should be Int32
    public enum WriteSeqFieldFlags : byte
#pragma warning restore CA1028 // Enum storage should be Int32
    {
        None = 0,
        ExtraParam = 0x1,
    }

    public abstract class GpuSource : ShaderDataBlock
    {
        public abstract string BlockName { get; }
        public int SourceId { get; }
        public int Size { get; protected set; }
        public byte[] Sourcebytes { get; protected set; } = [];
        public Guid HashMD5 { get; protected set; }
        protected GpuSource(ShaderDataReader datareader, int sourceId) : base(datareader)
        {
            SourceId = sourceId;
            Size = datareader.ReadInt32();
        }

        public bool IsEmpty()
        {
            return Size == 0;
        }
    }

    public class GlslSource : GpuSource
    {
        public override string BlockName => "GLSL";
        public int Arg0 { get; } // always 3
        // offset2, if present, always observes offset2 == offset + 8
        // offset2 can also be interpreted as the source-size
        public int SizeText { get; } = -1;

        public GlslSource(ShaderDataReader datareader, int sourceId)
            : base(datareader, sourceId)
        {
            if (Size > 0)
            {
                Arg0 = DataReader.ReadInt32();
                SizeText = DataReader.ReadInt32();
                Sourcebytes = DataReader.ReadBytes(SizeText - 1); // -1 because the sourcebytes are null-term
                DataReader.BaseStream.Position += 1;
            }

            HashMD5 = new Guid(datareader.ReadBytes(16));
        }
    }

    public class DxilSource : GpuSource
    {
        public override string BlockName => "DXIL";
        public int Arg0 { get; } // always 3
        public int Arg1 { get; } // always 0xFFFF or 0xFFFE
        public int HeaderBytes { get; }

        public DxilSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            if (Size > 0)
            {
                Arg0 = datareader.ReadInt16();
                Arg1 = (int)datareader.ReadUInt16();
                uint dxilDelim = datareader.ReadUInt16();
                if (dxilDelim != 0xFFFE)
                {
                    throw new ShaderParserException($"Unexpected DXIL source id {dxilDelim:x08}");
                }

                HeaderBytes = (int)datareader.ReadUInt16() * 4; // size is given as a 4-byte count
                Sourcebytes = datareader.ReadBytes(Size - 8);
            }

            HashMD5 = new Guid(datareader.ReadBytes(16));
        }
    }

    /*
     * The DXBC sources only have one header, the offset (which happens to be equal to their source size)
     */
    public class DxbcSource : GpuSource
    {
        public override string BlockName => "DXBC";

        public DxbcSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            if (Size > 0)
            {
                Sourcebytes = datareader.ReadBytes(Size);
            }

            HashMD5 = new Guid(datareader.ReadBytes(16));
        }
    }

    public class VulkanSource : GpuSource
    {
        public override string BlockName => "VULKAN";
        public int Arg0 { get; } = -1;
        public int MetaDataSize { get; } = -1;
        public Span<byte> Bytecode => Sourcebytes.AsSpan()[0..MetaDataSize];
        public Span<byte> Metadata => Sourcebytes.AsSpan()[MetaDataSize..];

        public VulkanSource(ShaderDataReader datareader, int sourceId) : base(datareader, sourceId)
        {
            if (Size > 0)
            {
                Arg0 = datareader.ReadInt32();
                MetaDataSize = datareader.ReadInt32();
                Sourcebytes = datareader.ReadBytes(Size - 8);
            }

            HashMD5 = new Guid(datareader.ReadBytes(16));
        }
    }
}
