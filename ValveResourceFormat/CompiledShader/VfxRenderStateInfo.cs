using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Contains render state information for a shader variant.
/// </summary>
public class VfxRenderStateInfo
{
    /// <summary>Gets the dynamic combo ID.</summary>
    public long DynamicComboId { get; }

    /// <summary>Gets the shader file ID.</summary>
    public int ShaderFileId { get; }

    /// <summary>Gets the source pointer.</summary>
    public int SourcePointer { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxRenderStateInfo"/> class.
    /// </summary>
    public VfxRenderStateInfo(long comboId, int shaderId, int sourcePointer)
    {
        DynamicComboId = comboId;
        ShaderFileId = shaderId;
        SourcePointer = sourcePointer;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxRenderStateInfo"/> class from a binary reader.
    /// </summary>
    public VfxRenderStateInfo(BinaryReader datareader)
    {
        DynamicComboId = datareader.ReadInt64();
        ShaderFileId = datareader.ReadInt32();
        SourcePointer = datareader.ReadInt32();
    }
}

/// <summary>
/// Render state information specific to hull shaders.
/// </summary>
public class VfxRenderStateInfoHullShader : VfxRenderStateInfo
{
    /// <summary>Gets the hull shader argument.</summary>
    public byte HullShaderArg { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxRenderStateInfoHullShader"/> class.
    /// </summary>
    public VfxRenderStateInfoHullShader(BinaryReader datareader) : base(datareader)
    {
        HullShaderArg = datareader.ReadByte();
    }
}

/// <summary>
/// Render state information specific to pixel shaders, including rasterizer, depth/stencil, and blend states.
/// </summary>
public class VfxRenderStateInfoPixelShader : VfxRenderStateInfo
{
    /// <summary>
    /// Describes the rasterizer state configuration.
    /// </summary>
    public class RsRasterizerStateDesc
    {
        /// <summary>
        /// Specifies the fill mode for rendering.
        /// </summary>
        public enum RsFillMode : byte
        {
#pragma warning disable CS1591
            Solid = 0,
            Wireframe = 1,
#pragma warning restore CS1591
        }

        /// <summary>
        /// Specifies the cull mode for rendering.
        /// </summary>
        public enum RsCullMode : byte
        {
#pragma warning disable CS1591
            None = 0,
            Back = 1,
            Front = 2,
#pragma warning restore CS1591
        }

        /// <summary>Gets the fill mode.</summary>
        public RsFillMode FillMode { get; }

        /// <summary>Gets the cull mode.</summary>
        public RsCullMode CullMode { get; }

        /// <summary>Gets a value indicating whether depth clipping is enabled.</summary>
        public bool DepthClipEnable { get; }

        /// <summary>Gets a value indicating whether multisampling is enabled.</summary>
        public bool MultisampleEnable { get; }

        /// <summary>Gets the depth bias.</summary>
        public int DepthBias { get; }

        /// <summary>Gets the depth bias clamp.</summary>
        public float DepthBiasClamp { get; }

        /// <summary>Gets the slope-scaled depth bias.</summary>
        public float SlopeScaledDepthBias { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RsRasterizerStateDesc"/> class.
        /// </summary>
        public RsRasterizerStateDesc(ReadOnlySpan<int> rasterizerStateBits)
        {
            Debug.Assert(rasterizerStateBits.Length == 4);

            FillMode = (RsFillMode)(rasterizerStateBits[0] & 0xFF);
            CullMode = (RsCullMode)((rasterizerStateBits[0] >> 8) & 0xFF);
            DepthClipEnable = ((rasterizerStateBits[0] >> 16) & 1) != 0;
            MultisampleEnable = ((rasterizerStateBits[0] >> 24) & 1) != 0;

            DepthBias = rasterizerStateBits[1];
            DepthBiasClamp = BitConverter.Int32BitsToSingle(rasterizerStateBits[2]);
            SlopeScaledDepthBias = BitConverter.Int32BitsToSingle(rasterizerStateBits[3]);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RsRasterizerStateDesc"/> class from a binary reader.
        /// </summary>
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
    /// Describes the depth and stencil state configuration.
    /// </summary>
    public class RsDepthStencilStateDesc
    {
        /// <summary>
        /// Specifies comparison functions.
        /// </summary>
        public enum RsComparison : byte
        {
#pragma warning disable CS1591
            Never = 0,
            Less = 1,
            Equal = 2,
            LessEqual = 3,
            Greater = 4,
            NotEqual = 5,
            GreaterEqual = 6,
            Always = 7,
#pragma warning restore CS1591
        }

        /// <summary>
        /// Specifies stencil operations.
        /// </summary>
        public enum RsStencilOp : byte
        {
#pragma warning disable CS1591
            Keep = 0,
            Zero = 1,
            Replace = 2,
            IncrSat = 3,
            DecrSat = 4,
            Invert = 5,
            Incr = 6,
            Decr = 7,
#pragma warning restore CS1591
        }

        /// <summary>
        /// Specifies Hi-Z mode for Xbox 360.
        /// </summary>
        public enum RsHiZMode360 : byte
        {
#pragma warning disable CS1591
            HiZAutomatic = 0,
            HiZDisable = 1,
            HiZEnable = 2,
#pragma warning restore CS1591
        }

        /// <summary>
        /// Specifies Hi-Stencil comparison for Xbox 360.
        /// </summary>
        public enum RsHiStencilComparison360 : byte
        {
#pragma warning disable CS1591
            HiStencilCmpEqual = 0,
            HiStencilCmpNotEqual = 1,
#pragma warning restore CS1591
        }

        /// <summary>Gets a value indicating whether depth testing is enabled.</summary>
        public bool DepthTestEnable { get; }

        /// <summary>Gets a value indicating whether depth writes are enabled.</summary>
        public bool DepthWriteEnable { get; }

        /// <summary>Gets the depth comparison function.</summary>
        public RsComparison DepthFunc { get; }

        /// <summary>Gets the Hi-Z enable mode for Xbox 360.</summary>
        public RsHiZMode360 HiZEnable360 { get; }

        /// <summary>Gets the Hi-Z write enable mode for Xbox 360.</summary>
        public RsHiZMode360 HiZWriteEnable360 { get; }

        /// <summary>Gets a value indicating whether stencil testing is enabled.</summary>
        public bool StencilEnable { get; }

        /// <summary>Gets the stencil read mask.</summary>
        public byte StencilReadMask { get; }

        /// <summary>Gets the stencil write mask.</summary>
        public byte StencilWriteMask { get; }

        /// <summary>Gets the front-face stencil fail operation.</summary>
        public RsStencilOp FrontStencilFailOp { get; }

        /// <summary>Gets the front-face stencil depth-fail operation.</summary>
        public RsStencilOp FrontStencilDepthFailOp { get; }

        /// <summary>Gets the front-face stencil pass operation.</summary>
        public RsStencilOp FrontStencilPassOp { get; }

        /// <summary>Gets the front-face stencil function.</summary>
        public RsComparison FrontStencilFunc { get; }

        /// <summary>Gets the back-face stencil fail operation.</summary>
        public RsStencilOp BackStencilFailOp { get; }

        /// <summary>Gets the back-face stencil depth-fail operation.</summary>
        public RsStencilOp BackStencilDepthFailOp { get; }

        /// <summary>Gets the back-face stencil pass operation.</summary>
        public RsStencilOp BackStencilPassOp { get; }

        /// <summary>Gets the back-face stencil function.</summary>
        public RsComparison BackStencilFunc { get; }

        /// <summary>Gets a value indicating whether Hi-Stencil is enabled for Xbox 360.</summary>
        public bool HiStencilEnable360 { get; }

        /// <summary>Gets a value indicating whether Hi-Stencil write is enabled for Xbox 360.</summary>
        public bool HiStencilWriteEnable360 { get; }

        /// <summary>Gets the Hi-Stencil function for Xbox 360.</summary>
        public RsHiStencilComparison360 HiStencilFunc360 { get; }

        /// <summary>Gets the Hi-Stencil reference value for Xbox 360.</summary>
        public byte HiStencilRef360 { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RsDepthStencilStateDesc"/> class.
        /// </summary>
        public RsDepthStencilStateDesc(ulong depthStencilBits)
        {
            // Depth state
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

        /// <summary>
        /// Initializes a new instance of the <see cref="RsDepthStencilStateDesc"/> class from a binary reader.
        /// </summary>
        public RsDepthStencilStateDesc(BinaryReader datareader)
        {
            DepthTestEnable = datareader.ReadBoolean();
            DepthWriteEnable = datareader.ReadBoolean();
            DepthFunc = (RsComparison)datareader.ReadByte();

            HiZEnable360 = (RsHiZMode360)datareader.ReadByte();
            HiZWriteEnable360 = (RsHiZMode360)datareader.ReadByte();

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

            HiStencilEnable360 = datareader.ReadBoolean();
            HiStencilWriteEnable360 = datareader.ReadBoolean();
            HiStencilFunc360 = (RsHiStencilComparison360)datareader.ReadByte();
            HiStencilRef360 = datareader.ReadByte();
        }
    }

    /// <summary>
    /// Describes the blend state configuration for render targets.
    /// </summary>
    public class RsBlendStateDesc
    {
        /// <summary>Gets the maximum number of render targets.</summary>
        public const int MaxRenderTargets = 8;

        /// <summary>
        /// Specifies blend operations.
        /// </summary>
        public enum RsBlendOp : byte
        {
#pragma warning disable CS1591
            Add = 0,
            Subtract = 1,
            RevSubtract = 2,
            Min = 3,
            Max = 4,
#pragma warning restore CS1591
        }

        /// <summary>
        /// Specifies blend modes.
        /// </summary>
        public enum RsBlendMode : byte
        {
#pragma warning disable CS1591
            Zero = 0,
            One = 1,
            SrcColor = 2,
            InvSrcColor = 3,
            SrcAlpha = 4,
            InvSrcAlpha = 5,
            DestAlpha = 6,
            InvDestAlpha = 7,
            DestColor = 8,
            InvDestColor = 9,
            SrcAlphaSat = 10,
            BlendFactor = 11,
            InvBlendFactor = 12,
#pragma warning restore CS1591
        }

        /// <summary>
        /// Specifies color write enable flags.
        /// </summary>
        [Flags]
        public enum RsColorWriteEnableBits
        {
#pragma warning disable CS1591
            None = 0,
            R = 0x1,
            G = 0x2,
            B = 0x4,
            A = 0x8,
            All = R | G | B | A
#pragma warning restore CS1591
        }

        /// <summary>Gets a value indicating whether alpha-to-coverage is enabled.</summary>
        public bool AlphaToCoverageEnable { get; }

        /// <summary>Gets a value indicating whether independent blending per render target is enabled.</summary>
        public bool IndependentBlendEnable { get; }

        /// <summary>Gets the high precision blend enable value for Xbox 360.</summary>
        public byte HighPrecisionBlendEnable360 { get; }

        /// <summary>Gets an array indicating whether blending is enabled for each render target.</summary>
        public bool[] BlendEnable { get; } = new bool[MaxRenderTargets];

        /// <summary>Gets an array of source blend modes for each render target.</summary>
        public RsBlendMode[] SrcBlend { get; } = new RsBlendMode[MaxRenderTargets];

        /// <summary>Gets an array of destination blend modes for each render target.</summary>
        public RsBlendMode[] DestBlend { get; } = new RsBlendMode[MaxRenderTargets];

        /// <summary>Gets an array of blend operations for each render target.</summary>
        public RsBlendOp[] BlendOp { get; } = new RsBlendOp[MaxRenderTargets];

        /// <summary>Gets an array of source alpha blend modes for each render target.</summary>
        public RsBlendMode[] SrcBlendAlpha { get; } = new RsBlendMode[MaxRenderTargets];

        /// <summary>Gets an array of destination alpha blend modes for each render target.</summary>
        public RsBlendMode[] DestBlendAlpha { get; } = new RsBlendMode[MaxRenderTargets];

        /// <summary>Gets an array of alpha blend operations for each render target.</summary>
        public RsBlendOp[] BlendOpAlpha { get; } = new RsBlendOp[MaxRenderTargets];

        /// <summary>Gets an array of render target write masks for each render target.</summary>
        public RsColorWriteEnableBits[] RenderTargetWriteMask { get; } = new RsColorWriteEnableBits[MaxRenderTargets];

        /// <summary>Gets an array indicating whether sRGB write is enabled for each render target.</summary>
        public bool[] SrgbWriteEnable { get; } = new bool[MaxRenderTargets];

        /// <summary>
        /// Initializes a new instance of the <see cref="RsBlendStateDesc"/> class.
        /// </summary>
        public RsBlendStateDesc(ReadOnlySpan<int> blendStateBits)
        {
            Debug.Assert(blendStateBits.Length == 8);

            // Extract per-render target values from packed bits
            for (var i = 0; i < MaxRenderTargets; i++)
            {
                SrcBlend[i] = (RsBlendMode)((blendStateBits[0] >> (i * 4)) & 0xF);
                DestBlend[i] = (RsBlendMode)((blendStateBits[1] >> (i * 4)) & 0xF);
                SrcBlendAlpha[i] = (RsBlendMode)((blendStateBits[2] >> (i * 4)) & 0xF);
                DestBlendAlpha[i] = (RsBlendMode)((blendStateBits[3] >> (i * 4)) & 0xF);
                RenderTargetWriteMask[i] = (RsColorWriteEnableBits)((blendStateBits[4] >> (i * 4)) & 0xF);
                BlendOp[i] = (RsBlendOp)((blendStateBits[5] >> (i * 4)) & 0xF);  // First 30 bits
                BlendOpAlpha[i] = (RsBlendOp)((blendStateBits[6] >> (i * 4)) & 0xF);
            }

            // Bitfields at the end of blendStateBits[5]
            AlphaToCoverageEnable = ((blendStateBits[5] >> 30) & 1) != 0;
            IndependentBlendEnable = ((blendStateBits[5] >> 31) & 1) != 0;

            // Last int contains blend enable and srgb write enable bytes
            for (var i = 0; i < MaxRenderTargets; i++)
            {
                BlendEnable[i] = ((blendStateBits[7] >> i) & 1) != 0;
                SrgbWriteEnable[i] = ((blendStateBits[7] >> (8 + i)) & 1) != 0;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RsBlendStateDesc"/> class from a binary reader.
        /// </summary>
        public RsBlendStateDesc(BinaryReader datareader)
        {
            AlphaToCoverageEnable = datareader.ReadBoolean();
            IndependentBlendEnable = datareader.ReadBoolean();
            HighPrecisionBlendEnable360 = datareader.ReadByte(); // might be wrong/unused

            for (var i = 0; i < MaxRenderTargets; i++)
            {
                BlendEnable[i] = datareader.ReadBoolean();
            }

            for (var i = 0; i < MaxRenderTargets; i++)
            {
                SrcBlend[i] = (RsBlendMode)datareader.ReadByte();
            }

            for (var i = 0; i < MaxRenderTargets; i++)
            {
                DestBlend[i] = (RsBlendMode)datareader.ReadByte();
            }

            for (var i = 0; i < MaxRenderTargets; i++)
            {
                BlendOp[i] = (RsBlendOp)datareader.ReadByte();
            }

            for (var i = 0; i < MaxRenderTargets; i++)
            {
                SrcBlendAlpha[i] = (RsBlendMode)datareader.ReadByte();
            }

            for (var i = 0; i < MaxRenderTargets; i++)
            {
                DestBlendAlpha[i] = (RsBlendMode)datareader.ReadByte();
            }

            for (var i = 0; i < MaxRenderTargets; i++)
            {
                BlendOpAlpha[i] = (RsBlendOp)datareader.ReadByte();
            }

            for (var i = 0; i < MaxRenderTargets; i++)
            {
                RenderTargetWriteMask[i] = (RsColorWriteEnableBits)(datareader.ReadByte() & 0xF);
            }

            for (var i = 0; i < MaxRenderTargets; i++)
            {
                SrgbWriteEnable[i] = datareader.ReadBoolean();
            }
        }
    }

    /// <summary>Gets the rasterizer state descriptor.</summary>
    public RsRasterizerStateDesc? RasterizerStateDesc { get; }

    /// <summary>Gets the depth/stencil state descriptor.</summary>
    public RsDepthStencilStateDesc? DepthStencilStateDesc { get; }

    /// <summary>Gets the blend state descriptor.</summary>
    public RsBlendStateDesc? BlendStateDesc { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxRenderStateInfoPixelShader"/> class.
    /// </summary>
    public VfxRenderStateInfoPixelShader(long comboId, int shaderId, int sourcePointer, KVObject renderState)
        : base(comboId, shaderId, sourcePointer)
    {
        if (renderState is null)
        {
            return;
        }

        if (renderState.ContainsKey("rasterizerStateDesc"))
        {
            RasterizerStateDesc = new RsRasterizerStateDesc(renderState.GetArray<int>("rasterizerStateDesc"));
        }

        if (renderState.ContainsKey("depthStencilStateDesc"))
        {
            DepthStencilStateDesc = new RsDepthStencilStateDesc(renderState.GetUnsignedIntegerProperty("depthStencilStateDesc"));
        }

        // PSRS only has blendStateDesc
        if (renderState.ContainsKey("blendStateDesc"))
        {
            BlendStateDesc = new RsBlendStateDesc(renderState.GetArray<int>("blendStateDesc"));
        }
    }

    public VfxRenderStateInfoPixelShader(BinaryReader datareader) : base(datareader)
    {
        var hasRasterizerState = !datareader.ReadBoolean();
        var hasDepthStencilState = !datareader.ReadBoolean();
        var hasBlendState = !datareader.ReadBoolean();

        if (hasRasterizerState)
        {
            RasterizerStateDesc = new RsRasterizerStateDesc(datareader);
        }

        if (hasDepthStencilState)
        {
            DepthStencilStateDesc = new RsDepthStencilStateDesc(datareader);
        }

        if (hasBlendState)
        {
            BlendStateDesc = new RsBlendStateDesc(datareader);
        }
    }
}
