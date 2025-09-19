using System.IO;

namespace ValveResourceFormat.CompiledShader;

public class VfxRenderStateInfo
{
    public long DynamicComboId { get; }
    public int ShaderFileId { get; }
    public int SourcePointer { get; }

    public VfxRenderStateInfo(long comboId, int shaderId, int sourcePointer)
    {
        DynamicComboId = comboId;
        ShaderFileId = shaderId;
        SourcePointer = sourcePointer;
    }

    public VfxRenderStateInfo(BinaryReader datareader)
    {
        DynamicComboId = datareader.ReadInt64();
        ShaderFileId = datareader.ReadInt32();
        SourcePointer = datareader.ReadInt32();
    }
}

public class VfxRenderStateInfoHullShader : VfxRenderStateInfo
{
    public byte HullShaderArg { get; }

    public VfxRenderStateInfoHullShader(BinaryReader datareader) : base(datareader)
    {
        HullShaderArg = datareader.ReadByte();
    }
}

public class VfxRenderStateInfoPixelShader : VfxRenderStateInfo
{
    public class RsRasterizerStateDesc
    {
        public enum RsFillMode : byte
        {
            Solid = 0,
            Wireframe = 1,
        }

        public enum RsCullMode : byte
        {
            None = 0,
            Back = 1,
            Front = 2,
        }

        public RsFillMode FillMode { get; }
        public RsCullMode CullMode { get; }
        public bool DepthClipEnable { get; }
        public bool MultisampleEnable { get; }
        public int DepthBias { get; }
        public float DepthBiasClamp { get; }
        public float SlopeScaledDepthBias { get; }

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

    public class RsDepthStencilStateDesc
    {
        public enum RsComparison : byte
        {
            Never = 0,
            Less = 1,
            Equal = 2,
            LessEqual = 3,
            Greater = 4,
            NotEqual = 5,
            GreaterEqual = 6,
            Always = 7,
        }

        public enum RsStencilOp : byte
        {
            Keep = 0,
            Zero = 1,
            Replace = 2,
            IncrSat = 3,
            DecrSat = 4,
            Invert = 5,
            Incr = 6,
            Decr = 7,
        }

        public enum RsHiZMode360 : byte
        {
            HiZAutomatic = 0,
            HiZDisable = 1,
            HiZEnable = 2,
        }

        public enum RsHiStencilComparison360 : byte
        {
            HiStencilCmpEqual = 0,
            HiStencilCmpNotEqual = 1,
        }

        public bool DepthTestEnable { get; }
        public bool DepthWriteEnable { get; }
        public RsComparison DepthFunc { get; }
        public RsHiZMode360 HiZEnable360 { get; }
        public RsHiZMode360 HiZWriteEnable360 { get; }
        public bool StencilEnable { get; }
        public byte StencilReadMask { get; }
        public byte StencilWriteMask { get; }
        public RsStencilOp FrontStencilFailOp { get; }
        public RsStencilOp FrontStencilDepthFailOp { get; }
        public RsStencilOp FrontStencilPassOp { get; }
        public RsComparison FrontStencilFunc { get; }
        public RsStencilOp BackStencilFailOp { get; }
        public RsStencilOp BackStencilDepthFailOp { get; }
        public RsStencilOp BackStencilPassOp { get; }
        public RsComparison BackStencilFunc { get; }
        public bool HiStencilEnable360 { get; }
        public bool HiStencilWriteEnable360 { get; }
        public RsHiStencilComparison360 HiStencilFunc360 { get; }
        public byte HiStencilRef360 { get; }

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

    public class RsBlendStateDesc
    {
        private const int MaxRenderTargets = 8;

        public enum RsBlendOp : byte
        {
            Add = 0,
            Subtract = 1,
            RevSubtract = 2,
            Min = 3,
            Max = 4,
        }

        public enum RsBlendMode : byte
        {
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
        }

        [Flags]
        public enum RsColorWriteEnableBits
        {
            None = 0,
            R = 0x1,
            G = 0x2,
            B = 0x4,
            A = 0x8,
            All = R | G | B | A
        }

        public bool AlphaToCoverageEnable { get; }
        public bool IndependentBlendEnable { get; }
        public byte HighPrecisionBlendEnable360 { get; }

        public bool[] BlendEnable { get; } = new bool[MaxRenderTargets];
        public RsBlendMode[] SrcBlend { get; } = new RsBlendMode[MaxRenderTargets];
        public RsBlendMode[] DestBlend { get; } = new RsBlendMode[MaxRenderTargets];
        public RsBlendOp[] BlendOp { get; } = new RsBlendOp[MaxRenderTargets];
        public RsBlendMode[] SrcBlendAlpha { get; } = new RsBlendMode[MaxRenderTargets];
        public RsBlendMode[] DestBlendAlpha { get; } = new RsBlendMode[MaxRenderTargets];
        public RsBlendOp[] BlendOpAlpha { get; } = new RsBlendOp[MaxRenderTargets];
        public RsColorWriteEnableBits[] RenderTargetWriteMask { get; } = new RsColorWriteEnableBits[MaxRenderTargets];
        public bool[] SrgbWriteEnable { get; } = new bool[MaxRenderTargets];

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

    public RsRasterizerStateDesc? RasterizerStateDesc { get; }
    public RsDepthStencilStateDesc? DepthStencilStateDesc { get; }
    public RsBlendStateDesc? BlendStateDesc { get; }

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
