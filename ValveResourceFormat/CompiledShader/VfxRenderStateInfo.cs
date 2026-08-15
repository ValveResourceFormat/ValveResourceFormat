using System.IO;
using ValveKeyValue;
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
    /// <summary>Gets the rasterizer state descriptor.</summary>
    public RsRasterizerStateDesc? RasterizerStateDesc { get; }

    /// <summary>Gets the depth/stencil state descriptor.</summary>
    public RsDepthStencilStateDesc? DepthStencilStateDesc { get; }

    /// <summary>Gets the blend state descriptor.</summary>
    public RsBlendStateDesc? BlendStateDesc { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxRenderStateInfoPixelShader"/> class.
    /// </summary>
    public VfxRenderStateInfoPixelShader(long comboId, int shaderId, int sourcePointer, KVObject renderState, int vcsVersion)
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
            DepthStencilStateDesc = new RsDepthStencilStateDesc(renderState.GetUnsignedIntegerProperty("depthStencilStateDesc"), vcsVersion);
        }

        // PSRS only has blendStateDesc
        if (renderState.ContainsKey("blendStateDesc"))
        {
            BlendStateDesc = new RsBlendStateDesc(renderState.GetArray<int>("blendStateDesc"));
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxRenderStateInfoPixelShader"/> class from a binary reader.
    /// </summary>
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
