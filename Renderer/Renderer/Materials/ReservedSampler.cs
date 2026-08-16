using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.Materials;

/// <summary>
/// The sampler types a shader declares a texture uniform with. Each one reads a different texture target, and
/// a bindless handle carries its target with it, so the two have to agree wherever a handle is written.
/// </summary>
public enum SamplerKind
{
    /// <summary><c>sampler2D</c>.</summary>
    Texture2D,
    /// <summary><c>sampler3D</c>.</summary>
    Texture3D,
    /// <summary><c>samplerCube</c>.</summary>
    TextureCube,
    /// <summary><c>sampler2DArray</c>.</summary>
    Texture2DArray,
    /// <summary><c>samplerCubeArray</c>.</summary>
    TextureCubeArray,
    /// <summary><c>sampler2DShadow</c>.</summary>
    Texture2DShadow,
    /// <summary><c>sampler2DArrayShadow</c>.</summary>
    Texture2DArrayShadow,
}

/// <summary>
/// A sampler uniform the renderer supplies for the whole scene rather than any one material.
/// </summary>
/// <param name="Name">The sampler uniform name, as shaders declare it.</param>
/// <param name="Kind">The sampler type shaders declare it with.</param>
public readonly record struct ReservedSampler(string Name, SamplerKind Kind);

/// <summary>
/// The sampler uniforms the scene fills rather than a material. Their handles go in the shared
/// <see cref="Buffers.SceneTextures"/> buffer, since packing a value every material shares into each material's
/// own buffer would mean rewriting all of them whenever the scene replaced one.
/// </summary>
public static class ReservedSamplers
{
    /// <summary>
    /// Gets the samplers the renderer sets once per frame, which are the members of the shared buffer.
    /// </summary>
    public static readonly ImmutableArray<ReservedSampler> Scene =
    [
        new("g_tBRDFLookup", SamplerKind.Texture2DArray),
        new("g_tBlueNoise", SamplerKind.Texture2D),
        new("g_tFogCubeTexture", SamplerKind.TextureCube),

        // Baked lighting, whole or split per channel, from the lightmap.
        new("g_tIrradiance", SamplerKind.Texture2DArray),
        new("g_tDirectionalIrradiance", SamplerKind.Texture2DArray),
        new("g_tDirectionalIrradianceR", SamplerKind.Texture2DArray),
        new("g_tDirectionalIrradianceG", SamplerKind.Texture2DArray),
        new("g_tDirectionalIrradianceB", SamplerKind.Texture2DArray),
        new("g_tDirectLightIndices", SamplerKind.Texture2DArray),
        new("g_tDirectLightStrengths", SamplerKind.Texture2DArray),
        new("g_tDirectLightShadows", SamplerKind.Texture2DArray),
        new("g_tIrradianceDebugChart", SamplerKind.Texture2DArray),

        new("g_tShadowDepthBufferDepth", SamplerKind.Texture2DArrayShadow),
        new("g_tBarnLightShadowDepth", SamplerKind.Texture2DShadow),
        new("g_tLightCookieTexture", SamplerKind.Texture2DArray),
        new("g_tLightCookieTextureWrap", SamplerKind.Texture2DArray),
        new("g_tWetnessWaves", SamplerKind.Texture2D),
        new("g_tSceneColor", SamplerKind.Texture2D),
        new("g_tSceneDepth", SamplerKind.Texture2D),
    ];

    /// <summary>
    /// Gets the samplers the renderer picks per draw, from the node being drawn. These stay loose uniforms:
    /// writing one into the shared buffer would upload to it per draw, and no kind is recorded because a
    /// shader is free to declare one under more than one type, as <c>g_tEnvironmentMap</c> does.
    /// </summary>
    public static readonly FrozenSet<string> PerInstance = new[]
    {
        "g_tEnvironmentMap",
        "morphCompositeTexture",
        "g_tLPV_Irradiance",
        "g_tLPV_Indices",
        "g_tLPV_Scalars",
        "g_tLPV_Shadows",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Names =
        Scene.Select(static sampler => sampler.Name).Concat(PerInstance).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Returns whether a uniform name is one the renderer supplies rather than a material.</summary>
    /// <param name="uniformName">The sampler uniform name.</param>
    public static bool Contains(string uniformName) => Names.Contains(uniformName);

    /// <summary>Returns the texture target a sampler of the given type reads from.</summary>
    /// <param name="kind">The sampler type.</param>
    public static TextureTarget GetTextureTarget(SamplerKind kind) => kind switch
    {
        SamplerKind.Texture2D or SamplerKind.Texture2DShadow => TextureTarget.Texture2D,
        SamplerKind.Texture3D => TextureTarget.Texture3D,
        SamplerKind.TextureCube => TextureTarget.TextureCubeMap,
        SamplerKind.Texture2DArray or SamplerKind.Texture2DArrayShadow => TextureTarget.Texture2DArray,
        SamplerKind.TextureCubeArray => TextureTarget.TextureCubeMapArray,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Maps a GLSL sampler keyword onto the kind it names.</summary>
    /// <param name="glslType">The type keyword as it appears in the shader source.</param>
    /// <param name="kind">The matching kind when the keyword names a supported sampler.</param>
    /// <returns><see langword="true"/> when the keyword names a sampler this renderer can bind.</returns>
    public static bool TryGetKind(ReadOnlySpan<char> glslType, out SamplerKind kind)
    {
        // The integer sampler prefixes read the same targets, so they map onto the same kinds.
        if (glslType.StartsWith("u", StringComparison.Ordinal) || glslType.StartsWith("i", StringComparison.Ordinal))
        {
            glslType = glslType[1..];
        }

        switch (glslType)
        {
            case "sampler2D": kind = SamplerKind.Texture2D; return true;
            case "sampler3D": kind = SamplerKind.Texture3D; return true;
            case "samplerCube": kind = SamplerKind.TextureCube; return true;
            case "sampler2DArray": kind = SamplerKind.Texture2DArray; return true;
            case "samplerCubeArray": kind = SamplerKind.TextureCubeArray; return true;
            case "sampler2DShadow": kind = SamplerKind.Texture2DShadow; return true;
            case "sampler2DArrayShadow": kind = SamplerKind.Texture2DArrayShadow; return true;
            default: kind = default; return false;
        }
    }
}
