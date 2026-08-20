using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Maps the engine's graphics vocabulary to the OpenGL equivalents, the way
/// <see cref="GLImageFormatExtensions"/> does for image formats. Holding every API specific enum
/// behind these files is what lets <see cref="GraphicsDevice"/> be reimplemented on another API
/// without its callers changing.
///
/// A sibling for another API sits next to this one rather than replacing it. It will not be a
/// straight table on every enum: <see cref="TextureType"/> splits into an image type, a view type
/// and a layer count, and <see cref="QueryType.TimeElapsed"/> has to become a pair of timestamps
/// where the API has no elapsed query. The engine vocabulary is what stays fixed.
/// </summary>
public static class GLGraphicsTypeExtensions
{
    /// <summary>Returns the texture target a texture of this type is created and bound with.</summary>
    public static TextureTarget ToGLTextureTarget(this TextureType type) => type switch
    {
        TextureType.Texture2D => TextureTarget.Texture2D,
        TextureType.Texture2DArray => TextureTarget.Texture2DArray,
        TextureType.Texture2DMultisample => TextureTarget.Texture2DMultisample,
        TextureType.Texture3D => TextureTarget.Texture3D,
        TextureType.TextureCube => TextureTarget.TextureCubeMap,
        TextureType.TextureCubeArray => TextureTarget.TextureCubeMapArray,
        _ => throw new NotImplementedException($"Unsupported texture type {type}"),
    };

    /// <summary>Returns the query target a query of this type is created against.</summary>
    public static QueryTarget ToGLQueryTarget(this QueryType type) => type switch
    {
        QueryType.TimeElapsed => QueryTarget.TimeElapsed,
        QueryType.Timestamp => QueryTarget.Timestamp,
        QueryType.PrimitivesGenerated => QueryTarget.PrimitivesGenerated,
        _ => throw new NotImplementedException($"Unsupported query type {type}"),
    };

    /// <summary>Returns the shader object type for this pipeline stage.</summary>
    public static ShaderType ToGLShaderType(this ShaderProgramType stage) => stage switch
    {
        ShaderProgramType.Vertex => ShaderType.VertexShader,
        ShaderProgramType.Fragment => ShaderType.FragmentShader,
        ShaderProgramType.Compute => ShaderType.ComputeShader,
        _ => throw new NotImplementedException($"Unsupported shader stage {stage}"),
    };

    /// <summary>Returns the buffer target a buffer of this type binds to.</summary>
    public static BufferTarget ToGLBufferTarget(this BufferType type) => type switch
    {
        BufferType.Uniform => BufferTarget.UniformBuffer,
        BufferType.Storage => BufferTarget.ShaderStorageBuffer,
        BufferType.Indirect => BufferTarget.DrawIndirectBuffer,
        BufferType.IndirectCount => BufferTarget.ParameterBuffer,
        _ => throw new NotImplementedException($"Unsupported buffer type {type}"),
    };

    /// <summary>
    /// Returns the usage hint for this access pattern. GL takes a frequency and a direction where
    /// other APIs take a heap, so the hint is the closest pair rather than an exact equivalent.
    /// </summary>
    public static BufferUsageHint ToGLBufferUsageHint(this BufferUsage usage) => usage switch
    {
        BufferUsage.Static => BufferUsageHint.StaticDraw,
        BufferUsage.Dynamic => BufferUsageHint.DynamicDraw,
        BufferUsage.GpuOnly => BufferUsageHint.DynamicCopy,
        BufferUsage.Readback => BufferUsageHint.DynamicRead,
        _ => throw new NotImplementedException($"Unsupported buffer usage {usage}"),
    };
}
