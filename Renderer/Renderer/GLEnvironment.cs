using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// OpenGL environment initialization and default render state configuration.
/// </summary>
public static class GLEnvironment
{
    private const int VersionMajor = 4;
    private const int VersionMinor = 6;

    /// <summary>
    /// Minimum required OpenGL version (4.6).
    /// </summary>
    public static readonly Version RequiredVersion = new(VersionMajor, VersionMinor);

#if DEBUG
    /// <summary>
    /// Maximum length for OpenGL debug labels.
    /// </summary>
    public static int MaxLabelLength { get; private set; }
#endif

    private enum ParallelShaderCompileType : byte
    {
        None,
        Arb,
        Khr,
    }

    private static ParallelShaderCompileType ParallelShaderCompileSupport = ParallelShaderCompileType.None;

    /// <summary>
    /// Gets the GPU renderer name and driver version string.
    /// </summary>
    public static string? GpuRendererAndDriver { get; private set; }

    /// <summary>
    /// Initializes the OpenGL environment and queries capabilities.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <exception cref="NotSupportedException">Thrown if the OpenGL version is too old.</exception>
    public static void Initialize(ILogger logger)
    {
        if (GpuRendererAndDriver != null)
        {
            return;
        }

        var minor = GL.GetInteger(GetPName.MinorVersion);
        var major = GL.GetInteger(GetPName.MajorVersion);

        var gpu = $"GPU: {GL.GetString(StringName.Renderer)}, Driver: {GL.GetString(StringName.Version)}";

        GpuRendererAndDriver = gpu;

        logger.LogDebug("{Gpu}, OS: {OS}", gpu, Environment.OSVersion);

        MaterialLoader.MaxTextureMaxAnisotropy = GL.GetFloat((GetPName)ExtTextureFilterAnisotropic.MaxTextureMaxAnisotropyExt);

        if (major < VersionMajor || minor < VersionMinor)
        {
            throw new NotSupportedException($"Source 2 Viewer requires OpenGL {VersionMajor}.{VersionMinor}, but you have {major}.{minor}.");
        }

        var extensionCount = GL.GetInteger(GetPName.NumExtensions);
        var extensions = new HashSet<string>(extensionCount);
        for (var i = 0; i < extensionCount; i++)
        {
            var extension = GL.GetString(StringNameIndexed.Extensions, i);
            extensions.Add(extension);
        }

        if (extensions.Contains("GL_KHR_parallel_shader_compile"))
        {
            ParallelShaderCompileSupport = ParallelShaderCompileType.Khr;
        }
        else if (extensions.Contains("GL_ARB_parallel_shader_compile"))
        {
            ParallelShaderCompileSupport = ParallelShaderCompileType.Arb;
        }
        else
        {
            logger.LogWarning("Parallel shader compilation is not supported");
        }

#if DEBUG
        MaxLabelLength = GL.GetInteger(GetPName.MaxLabelLength) - 1;
#endif
    }

    /// <summary>
    /// Sets the default OpenGL render state for Source 2 rendering.
    /// </summary>
    public static void SetDefaultRenderState()
    {
        // Application semantics / default state
        GL.Enable(EnableCap.TextureCubeMapSeamless);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.Enable(EnableCap.DepthTest);

        // reverse z
        GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne);
        GL.DepthFunc(DepthFunction.Greater);
        GL.ClearDepth(0.0f);

        // Parallel shader compilation, 0xFFFFFFFF requests an implementation-specific maximum
        if (GLEnvironment.ParallelShaderCompileSupport == GLEnvironment.ParallelShaderCompileType.Khr)
        {
            GL.Khr.MaxShaderCompilerThreads(uint.MaxValue);
        }
        else if (GLEnvironment.ParallelShaderCompileSupport == GLEnvironment.ParallelShaderCompileType.Arb)
        {
            GL.Arb.MaxShaderCompilerThreads(uint.MaxValue);
        }
    }

    /// <summary>
    /// Converts a <see cref="Matrix4x4"/> to an OpenTK Matrix4.
    /// </summary>
    public static OpenTK.Mathematics.Matrix4 ToOpenTK(this Matrix4x4 m)
    {
        return new OpenTK.Mathematics.Matrix4(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);
    }

    /// <summary>
    /// Converts a <see cref="Matrix4x4"/> to an OpenTK Matrix3x4 by dropping the last row.
    /// </summary>
    public static OpenTK.Mathematics.Matrix3x4 To3x4(this Matrix4x4 m)
    {
        return new OpenTK.Mathematics.Matrix3x4(
            m.M11, m.M21, m.M31, m.M41,
            m.M12, m.M22, m.M32, m.M42,
            m.M13, m.M23, m.M33, m.M43
        );
    }
}
