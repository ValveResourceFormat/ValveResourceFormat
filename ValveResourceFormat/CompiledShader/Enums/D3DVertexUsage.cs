namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Vertex attribute usage, as packed into <see cref="VfxShaderFileVulkan.AttribMap"/> to identify vertex
/// shader inputs. These follow the legacy <c>D3DDECLUSAGE</c> values, except that the fixed function only
/// <c>POSITIONT</c> is absent, which shifts everything from <see cref="Color"/> onwards down by one.
/// Only the usages up to and including <see cref="Color"/> have been observed in shipped shaders.
/// </summary>
public enum D3DVertexUsage
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    Position,
    BlendWeight,
    BlendIndices,
    Normal,
    PSize,
    TexCoord,
    Tangent,
    Binormal,
    TessFactor,
    Color,
    Fog,
    Depth,
    Sample,
#pragma warning restore CS1591
}

/// <summary>
/// Extensions for <see cref="D3DVertexUsage"/>.
/// </summary>
internal static class D3DVertexUsageExtensions
{
    /// <summary>
    /// Gets the Direct3D semantic name for a vertex usage, as it appears in
    /// <see cref="ResourceTypes.Material.InputSignatureElement.D3DSemanticName"/>,
    /// or an empty string when the usage is unknown.
    /// </summary>
    public static string ToD3DSemanticName(this D3DVertexUsage usage) => usage switch
    {
        D3DVertexUsage.Position => "POSITION",
        D3DVertexUsage.BlendWeight => "BLENDWEIGHT",
        D3DVertexUsage.BlendIndices => "BLENDINDICES",
        D3DVertexUsage.Normal => "NORMAL",
        D3DVertexUsage.PSize => "PSIZE",
        D3DVertexUsage.TexCoord => "TEXCOORD",
        D3DVertexUsage.Tangent => "TANGENT",
        D3DVertexUsage.Binormal => "BINORMAL",
        D3DVertexUsage.TessFactor => "TESSFACTOR",
        D3DVertexUsage.Color => "COLOR",
        D3DVertexUsage.Fog => "FOG",
        D3DVertexUsage.Depth => "DEPTH",
        D3DVertexUsage.Sample => "SAMPLE",
        _ => string.Empty,
    };
}
