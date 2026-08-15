using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Describes the rasterizer state configuration.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/rendersystemdx11/RsRasterizerStateDesc_t">RsRasterizerStateDesc_t</seealso>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public record struct RsRasterizerStateDesc
{
    // fill mode (0-7) | cull mode (8-15) | depth clip (16) | multisample (24)
    private uint bits;

    /// <summary>Constant depth bias, in the smallest resolvable depth units.</summary>
    public int DepthBias { get; set; }

    /// <summary>Maximum total depth bias; 0 disables clamping.</summary>
    public float DepthBiasClamp { get; set; }

    /// <summary>Depth bias scaled by the polygon's depth slope.</summary>
    public float SlopeScaledDepthBias { get; set; }

    private readonly bool GetBit(int offset) => (bits & (1u << offset)) != 0;
    private void SetBit(int offset, bool value) => bits = value ? bits | (1u << offset) : bits & ~(1u << offset);

    private readonly uint GetBits(int offset, uint mask) => (bits >> offset) & mask;
    private void SetBits(int offset, uint mask, uint value) => bits = (bits & ~(mask << offset)) | ((value & mask) << offset);

    /// <summary>Polygon fill mode.</summary>
    public RsFillMode FillMode { readonly get => (RsFillMode)GetBits(0, 0xFF); set => SetBits(0, 0xFF, (byte)value); }

    /// <summary>Face culling mode.</summary>
    public RsCullMode CullMode { readonly get => (RsCullMode)GetBits(8, 0xFF); set => SetBits(8, 0xFF, (byte)value); }

    /// <summary>Whether geometry is clipped against the depth range.</summary>
    public bool DepthClipEnable { readonly get => GetBit(16); set => SetBit(16, value); }

    /// <summary>Whether multisampling is enabled.</summary>
    public bool MultisampleEnable { readonly get => GetBit(24); set => SetBit(24, value); }

    /// <summary>Reads the descriptor from packed bits.</summary>
    public RsRasterizerStateDesc(ReadOnlySpan<int> rasterizerStateBits)
    {
        Debug.Assert(rasterizerStateBits.Length == 4);

        bits = (uint)rasterizerStateBits[0];
        DepthBias = rasterizerStateBits[1];
        DepthBiasClamp = BitConverter.Int32BitsToSingle(rasterizerStateBits[2]);
        SlopeScaledDepthBias = BitConverter.Int32BitsToSingle(rasterizerStateBits[3]);
    }

    /// <summary>Reads the descriptor from a binary reader.</summary>
    public RsRasterizerStateDesc(BinaryReader datareader)
    {
        FillMode = (RsFillMode)datareader.ReadByte();
        CullMode = (RsCullMode)datareader.ReadByte();
        DepthClipEnable = datareader.ReadBoolean();
        MultisampleEnable = datareader.ReadBoolean();
        DepthBias = datareader.ReadInt32();
        DepthBiasClamp = datareader.ReadSingle();
        SlopeScaledDepthBias = datareader.ReadSingle();
    }
}

/// <summary>
/// Describes the depth and stencil state configuration. The stencil reference value is not part of
/// the descriptor; D3D and Vulkan set it at bind time.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/rendersystemdx11/RsDepthStencilStateDesc_t">RsDepthStencilStateDesc_t</seealso>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public record struct RsDepthStencilStateDesc
{
    // Version 71 layout:
    // 0 depth test | 1 depth write | 2-5 depth func | 16-19 front func | 20-23 back func
    // 24 stencil enable | 25-27 front fail | 28-30 front depth fail | 31-33 front pass
    // 34-36 back fail | 37-39 back depth fail | 40-42 back pass | 48-55 read mask | 56-63 write mask
    private ulong bits;

#pragma warning disable CS1591 // The properties below name what each mask covers
    public const ulong DepthTestEnableBits = 1UL << 0;
    public const ulong DepthWriteEnableBits = 1UL << 1;
    public const ulong DepthFuncBits = 0xFUL << 2;
    public const ulong FrontStencilFuncBits = 0xFUL << 16;
    public const ulong BackStencilFuncBits = 0xFUL << 20;
    public const ulong StencilEnableBits = 1UL << 24;
    public const ulong FrontStencilFailOpBits = 0x7UL << 25;
    public const ulong FrontStencilDepthFailOpBits = 0x7UL << 28;
    public const ulong FrontStencilPassOpBits = 0x7UL << 31;
    public const ulong BackStencilFailOpBits = 0x7UL << 34;
    public const ulong BackStencilDepthFailOpBits = 0x7UL << 37;
    public const ulong BackStencilPassOpBits = 0x7UL << 40;
    public const ulong StencilReadMaskBits = 0xFFUL << 48;
    public const ulong StencilWriteMaskBits = 0xFFUL << 56;
#pragma warning restore CS1591

    /// <summary>Returns the bits that differ between this descriptor and another, so a caller can
    /// test a field's mask to see whether it changed.</summary>
    /// <param name="other">The descriptor to compare against.</param>
    public readonly ulong Delta(in RsDepthStencilStateDesc other) => bits ^ other.bits;

    private readonly bool GetFlag(ulong mask) => (bits & mask) != 0;
    private void SetFlag(ulong mask, bool value) => bits = value ? bits | mask : bits & ~mask;

    private readonly uint Get(ulong mask) => (uint)((bits & mask) >> BitOperations.TrailingZeroCount(mask));
    private void Set(ulong mask, uint value) => bits = (bits & ~mask) | (((ulong)value << BitOperations.TrailingZeroCount(mask)) & mask);

    /// <summary>Whether depth testing is enabled.</summary>
    public bool DepthTestEnable { readonly get => GetFlag(DepthTestEnableBits); set => SetFlag(DepthTestEnableBits, value); }

    /// <summary>Whether depth writes are enabled.</summary>
    public bool DepthWriteEnable { readonly get => GetFlag(DepthWriteEnableBits); set => SetFlag(DepthWriteEnableBits, value); }

    /// <summary>Depth comparison function.</summary>
    public RsComparison DepthFunc { readonly get => (RsComparison)Get(DepthFuncBits); set => Set(DepthFuncBits, (uint)value); }

    /// <summary>Whether stencil testing is enabled.</summary>
    public bool StencilEnable { readonly get => GetFlag(StencilEnableBits); set => SetFlag(StencilEnableBits, value); }

    /// <summary>Mask applied to stored and reference values before comparison.</summary>
    public byte StencilReadMask { readonly get => (byte)Get(StencilReadMaskBits); set => Set(StencilReadMaskBits, value); }

    /// <summary>Mask of stencil bits that writes and stencil clears can touch.</summary>
    public byte StencilWriteMask { readonly get => (byte)Get(StencilWriteMaskBits); set => Set(StencilWriteMaskBits, value); }

    /// <summary>Front-face stencil comparison function.</summary>
    public RsComparison FrontStencilFunc { readonly get => (RsComparison)Get(FrontStencilFuncBits); set => Set(FrontStencilFuncBits, (uint)value); }

    /// <summary>Back-face stencil comparison function.</summary>
    public RsComparison BackStencilFunc { readonly get => (RsComparison)Get(BackStencilFuncBits); set => Set(BackStencilFuncBits, (uint)value); }

    /// <summary>Front-face operation when the stencil test fails.</summary>
    public RsStencilOp FrontStencilFailOp { readonly get => (RsStencilOp)Get(FrontStencilFailOpBits); set => Set(FrontStencilFailOpBits, (uint)value); }

    /// <summary>Front-face operation when the stencil test passes but the depth test fails.</summary>
    public RsStencilOp FrontStencilDepthFailOp { readonly get => (RsStencilOp)Get(FrontStencilDepthFailOpBits); set => Set(FrontStencilDepthFailOpBits, (uint)value); }

    /// <summary>Front-face operation when both stencil and depth tests pass.</summary>
    public RsStencilOp FrontStencilPassOp { readonly get => (RsStencilOp)Get(FrontStencilPassOpBits); set => Set(FrontStencilPassOpBits, (uint)value); }

    /// <summary>Back-face operation when the stencil test fails.</summary>
    public RsStencilOp BackStencilFailOp { readonly get => (RsStencilOp)Get(BackStencilFailOpBits); set => Set(BackStencilFailOpBits, (uint)value); }

    /// <summary>Back-face operation when the stencil test passes but the depth test fails.</summary>
    public RsStencilOp BackStencilDepthFailOp { readonly get => (RsStencilOp)Get(BackStencilDepthFailOpBits); set => Set(BackStencilDepthFailOpBits, (uint)value); }

    /// <summary>Back-face operation when both stencil and depth tests pass.</summary>
    public RsStencilOp BackStencilPassOp { readonly get => (RsStencilOp)Get(BackStencilPassOpBits); set => Set(BackStencilPassOpBits, (uint)value); }

    /// <summary>Reads the descriptor from packed bits.</summary>
    /// <param name="depthStencilBits">The packed bitfield value.</param>
    /// <param name="vcsVersion">The VCS version, which selects the bit layout.</param>
    public RsDepthStencilStateDesc(ulong depthStencilBits, int vcsVersion)
    {
        if (vcsVersion >= 71)
        {
            bits = depthStencilBits;
            return;
        }

        // Old layout, re-encode into the version 71 layout
        DepthTestEnable = (depthStencilBits & 1) != 0;
        DepthWriteEnable = ((depthStencilBits >> 1) & 1) != 0;
        DepthFunc = (RsComparison)((depthStencilBits >> 8) & 0xFF);

        // Stencil state starts at byte 2 (bit 16)
        // RsStencilStateDesc_t
        var stencilBits = depthStencilBits >> 16;

        StencilEnable = (stencilBits & 1) != 0;
        FrontStencilFailOp = (RsStencilOp)((stencilBits >> 1) & 0x7);
        FrontStencilDepthFailOp = (RsStencilOp)((stencilBits >> 4) & 0x7);
        FrontStencilPassOp = (RsStencilOp)((stencilBits >> 7) & 0x7);
        FrontStencilFunc = (RsComparison)((stencilBits >> 10) & 0x7);
        BackStencilFailOp = (RsStencilOp)((stencilBits >> 13) & 0x7);
        BackStencilDepthFailOp = (RsStencilOp)((stencilBits >> 16) & 0x7);
        BackStencilPassOp = (RsStencilOp)((stencilBits >> 19) & 0x7);
        BackStencilFunc = (RsComparison)((stencilBits >> 22) & 0x7);

        StencilReadMask = (byte)((stencilBits >> 32) & 0xFF);
        StencilWriteMask = (byte)((stencilBits >> 40) & 0xFF);
    }

    /// <summary>Reads the descriptor from a binary reader.</summary>
    public RsDepthStencilStateDesc(BinaryReader datareader)
    {
        DepthTestEnable = datareader.ReadBoolean();
        DepthWriteEnable = datareader.ReadBoolean();
        DepthFunc = (RsComparison)datareader.ReadByte();

        // Xbox 360 Hi-Z enable and write enable. The Source 2 struct has no such fields.
        datareader.BaseStream.Position += 2;

        StencilEnable = datareader.ReadBoolean();
        StencilReadMask = datareader.ReadByte();
        StencilWriteMask = datareader.ReadByte();

        FrontStencilFailOp = (RsStencilOp)datareader.ReadByte();
        FrontStencilDepthFailOp = (RsStencilOp)datareader.ReadByte();
        FrontStencilPassOp = (RsStencilOp)datareader.ReadByte();
        FrontStencilFunc = (RsComparison)datareader.ReadByte();

        BackStencilFailOp = (RsStencilOp)datareader.ReadByte();
        BackStencilDepthFailOp = (RsStencilOp)datareader.ReadByte();
        BackStencilPassOp = (RsStencilOp)datareader.ReadByte();
        BackStencilFunc = (RsComparison)datareader.ReadByte();

        // Xbox 360 Hi-Stencil enable, write enable, func and ref.
        datareader.BaseStream.Position += 4;
    }
}

/// <summary>
/// Indexes one fixed-width value per render target, read out of a single packed word.
/// </summary>
/// <typeparam name="T">The byte-backed enum the values decode to.</typeparam>
public readonly struct PerRenderTarget<T> where T : unmanaged, Enum
{
    private readonly uint word;
    private readonly int width;

    internal PerRenderTarget(uint word, int width)
    {
        this.word = word;
        this.width = width;
    }

    /// <summary>Gets the value of a render target.</summary>
    /// <param name="rt">The render target index.</param>
    public T this[int rt] => Unsafe.BitCast<byte, T>((byte)((word >> (rt * width)) & ((1u << width) - 1)));
}

/// <summary>
/// Indexes one bit per render target, read out of a shared word at a fixed offset.
/// </summary>
public readonly struct PerRenderTargetFlags
{
    private readonly uint word;
    private readonly int offset;

    internal PerRenderTargetFlags(uint word, int offset)
    {
        this.word = word;
        this.offset = offset;
    }

    /// <summary>Gets whether the flag is set for a render target.</summary>
    /// <param name="rt">The render target index.</param>
    public bool this[int rt] => (word & (1u << (offset + rt))) != 0;
}

/// <summary>
/// Describes the blend state configuration for all render targets, stored as the packed VCS words:
/// one 4-bit value per render target in each word.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/rendersystemdx11/RsBlendStateDesc_t">RsBlendStateDesc_t</seealso>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public record struct RsBlendStateDesc
{
    /// <summary>The maximum number of render targets.</summary>
    public const int MaxRenderTargets = 8;

    private uint srcBlendBits;
    private uint destBlendBits;
    private uint srcBlendAlphaBits;
    private uint destBlendAlphaBits;
    private uint renderTargetWriteMaskBits;
    private uint blendOpBits;      // 3-bit values in 0-23, of the 30 bits the schema gives them | 30 alpha to coverage | 31 independent blend
    private uint blendOpAlphaBits;
    private uint enableBits;       // 0-7 blend enable | 8-15 srgb write

    // Blend modes and write masks take a whole word at 4 bits each. Blend ops share their word with
    // the two flags below, so they only get 3 bits, which is all RsBlendOp needs.
    private const int BlendModeWidth = 4;
    private const int BlendOpWidth = 3;
    private const int WriteMaskWidth = 4;

    private static uint GetLane(uint word, int rt, int width) => (word >> (rt * width)) & ((1u << width) - 1);
    private static uint SetLane(uint word, int rt, int width, uint value)
    {
        var mask = (1u << width) - 1;
        return (word & ~(mask << (rt * width))) | ((value & mask) << (rt * width));
    }

    private readonly bool GetFlag(int offset) => (enableBits & (1u << offset)) != 0;
    private void SetFlag(int offset, bool value) => enableBits = value ? enableBits | (1u << offset) : enableBits & ~(1u << offset);

    /// <summary>Whether MSAA alpha-to-coverage is enabled.</summary>
    public bool AlphaToCoverageEnable
    {
        readonly get => (blendOpBits & (1u << 30)) != 0;
        set => blendOpBits = value ? blendOpBits | (1u << 30) : blendOpBits & ~(1u << 30);
    }

    /// <summary>Whether each render target blends independently.</summary>
    public bool IndependentBlendEnable
    {
        readonly get => (blendOpBits & (1u << 31)) != 0;
        set => blendOpBits = value ? blendOpBits | (1u << 31) : blendOpBits & ~(1u << 31);
    }

    // The indexers only read: C# rejects assigning through an indexer on a property return
    // (CS1612), so writes go through the Set methods.

    /// <summary>Whether blending is enabled, per render target.</summary>
    public readonly PerRenderTargetFlags BlendEnable => new(enableBits, 0);
    /// <summary>Sets whether blending is enabled for a render target.</summary>
    public void SetBlendEnable(int rt, bool value) => SetFlag(rt, value);

    /// <summary>Whether sRGB write is enabled, per render target.</summary>
    public readonly PerRenderTargetFlags SrgbWriteEnable => new(enableBits, 8);
    /// <summary>Sets whether sRGB write is enabled for a render target.</summary>
    public void SetSrgbWriteEnable(int rt, bool value) => SetFlag(8 + rt, value);

    /// <summary>Source blend mode, per render target.</summary>
    public readonly PerRenderTarget<RsBlendMode> SrcBlend => new(srcBlendBits, BlendModeWidth);
    /// <summary>Sets the source blend mode of a render target.</summary>
    public void SetSrcBlend(int rt, RsBlendMode value) => srcBlendBits = SetLane(srcBlendBits, rt, BlendModeWidth, (uint)value);

    /// <summary>Destination blend mode, per render target.</summary>
    public readonly PerRenderTarget<RsBlendMode> DestBlend => new(destBlendBits, BlendModeWidth);
    /// <summary>Sets the destination blend mode of a render target.</summary>
    public void SetDestBlend(int rt, RsBlendMode value) => destBlendBits = SetLane(destBlendBits, rt, BlendModeWidth, (uint)value);

    /// <summary>Source alpha blend mode, per render target.</summary>
    public readonly PerRenderTarget<RsBlendMode> SrcBlendAlpha => new(srcBlendAlphaBits, BlendModeWidth);
    /// <summary>Sets the source alpha blend mode of a render target.</summary>
    public void SetSrcBlendAlpha(int rt, RsBlendMode value) => srcBlendAlphaBits = SetLane(srcBlendAlphaBits, rt, BlendModeWidth, (uint)value);

    /// <summary>Destination alpha blend mode, per render target.</summary>
    public readonly PerRenderTarget<RsBlendMode> DestBlendAlpha => new(destBlendAlphaBits, BlendModeWidth);
    /// <summary>Sets the destination alpha blend mode of a render target.</summary>
    public void SetDestBlendAlpha(int rt, RsBlendMode value) => destBlendAlphaBits = SetLane(destBlendAlphaBits, rt, BlendModeWidth, (uint)value);

    /// <summary>Blend operation, per render target.</summary>
    public readonly PerRenderTarget<RsBlendOp> BlendOp => new(blendOpBits, BlendOpWidth);
    /// <summary>Sets the blend operation of a render target.</summary>
    public void SetBlendOp(int rt, RsBlendOp value) => blendOpBits = SetLane(blendOpBits, rt, BlendOpWidth, (uint)value);

    /// <summary>Alpha blend operation, per render target.</summary>
    public readonly PerRenderTarget<RsBlendOp> BlendOpAlpha => new(blendOpAlphaBits, BlendOpWidth);
    /// <summary>Sets the alpha blend operation of a render target.</summary>
    public void SetBlendOpAlpha(int rt, RsBlendOp value) => blendOpAlphaBits = SetLane(blendOpAlphaBits, rt, BlendOpWidth, (uint)value);

    /// <summary>Color write mask, per render target.</summary>
    public readonly PerRenderTarget<RsColorWriteEnableBits> RenderTargetWriteMask => new(renderTargetWriteMaskBits, WriteMaskWidth);
    /// <summary>Sets the color write mask of a render target.</summary>
    public void SetRenderTargetWriteMask(int rt, RsColorWriteEnableBits value) => renderTargetWriteMaskBits = SetLane(renderTargetWriteMaskBits, rt, WriteMaskWidth, (uint)value);

    /// <summary>Reads the descriptor from packed bits.</summary>
    public RsBlendStateDesc(ReadOnlySpan<int> blendStateBits)
    {
        Debug.Assert(blendStateBits.Length == 8);

        srcBlendBits = (uint)blendStateBits[0];
        destBlendBits = (uint)blendStateBits[1];
        srcBlendAlphaBits = (uint)blendStateBits[2];
        destBlendAlphaBits = (uint)blendStateBits[3];
        renderTargetWriteMaskBits = (uint)blendStateBits[4];
        blendOpBits = (uint)blendStateBits[5];
        blendOpAlphaBits = (uint)blendStateBits[6];
        enableBits = (uint)blendStateBits[7];
    }

    /// <summary>Reads the descriptor from a binary reader.</summary>
    public RsBlendStateDesc(BinaryReader datareader)
    {
        AlphaToCoverageEnable = datareader.ReadBoolean();
        IndependentBlendEnable = datareader.ReadBoolean();
        datareader.BaseStream.Position += 1; // Xbox 360 high precision blend enable

        ReadFlags(datareader, ref enableBits, 0);
        ReadLanes(datareader, ref srcBlendBits, BlendModeWidth);
        ReadLanes(datareader, ref destBlendBits, BlendModeWidth);
        ReadLanes(datareader, ref blendOpBits, BlendOpWidth);
        ReadLanes(datareader, ref srcBlendAlphaBits, BlendModeWidth);
        ReadLanes(datareader, ref destBlendAlphaBits, BlendModeWidth);
        ReadLanes(datareader, ref blendOpAlphaBits, BlendOpWidth);
        ReadLanes(datareader, ref renderTargetWriteMaskBits, WriteMaskWidth);
        ReadFlags(datareader, ref enableBits, 8);

        static void ReadLanes(BinaryReader datareader, ref uint word, int width)
        {
            for (var i = 0; i < MaxRenderTargets; i++)
            {
                word = SetLane(word, i, width, datareader.ReadByte());
            }
        }

        static void ReadFlags(BinaryReader datareader, ref uint word, int offset)
        {
            for (var i = 0; i < MaxRenderTargets; i++)
            {
                if (datareader.ReadBoolean())
                {
                    word |= 1u << (offset + i);
                }
            }
        }
    }
}
