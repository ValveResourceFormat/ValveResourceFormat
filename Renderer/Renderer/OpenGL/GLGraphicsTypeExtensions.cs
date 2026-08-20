using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Maps the engine's own vocabulary to the OpenGL equivalents, the way
/// <see cref="GLImageFormatExtensions"/> does for image formats.
/// </summary>
public static class GLGraphicsTypeExtensions
{
    /// <summary>Returns the wrap mode for this addressing mode.</summary>
    public static TextureWrapMode ToGLTextureWrapMode(this RsTextureAddressMode mode) => mode switch
    {
        RsTextureAddressMode.Wrap => TextureWrapMode.Repeat,
        RsTextureAddressMode.Mirror => TextureWrapMode.MirroredRepeat,
        RsTextureAddressMode.Clamp => TextureWrapMode.ClampToEdge,
        RsTextureAddressMode.Border => TextureWrapMode.ClampToBorder,
        // Core since 4.4, but missing from the TextureWrapMode enum.
        RsTextureAddressMode.MirrorOnce => (TextureWrapMode)Version44.MirrorClampToEdge,
        _ => throw new NotImplementedException($"Unsupported address mode {mode}"),
    };

    /// <summary>Returns the shader object type for this pipeline stage.</summary>
    public static ShaderType ToGLShaderType(this ShaderProgramType stage) => stage switch
    {
        ShaderProgramType.Vertex => ShaderType.VertexShader,
        ShaderProgramType.Fragment => ShaderType.FragmentShader,
        ShaderProgramType.Compute => ShaderType.ComputeShader,
        _ => throw new NotImplementedException($"Unsupported shader stage {stage}"),
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
