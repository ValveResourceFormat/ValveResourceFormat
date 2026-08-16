using System.Threading;
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
    private static int parallelShaderCompileConfigured;

    /// <summary>
    /// Indicates whether indirect count draw calls are supported by the current driver.
    /// </summary>
    public static bool IndirectCountSupported { get; private set; }

    /// <summary>
    /// Indicates whether the driver does not perform efficiently with small sub-draws, making GPU-driven
    /// rendering slower than direct draws. Intel drivers also misassign gl_BaseInstance across
    /// sub-draws when baseVertex varies within one multidraw.
    /// </summary>
    public static bool SlowMultiDrawIndirect { get; private set; }

    /// <summary>
    /// Gets the GPU renderer name and driver version string.
    /// </summary>
    public static string? GpuRendererAndDriver { get; private set; }

    private static bool bindlessTexturesSupported;
    private static bool? bindlessTextures;

    /// <summary>
    /// Gets or sets a value indicating whether the slot bound texture path is taken even on a driver that
    /// supports bindless textures. Read by <see cref="Initialize"/>, so it has to be set before the first
    /// GL context is created and changing it later does nothing.
    /// </summary>
    public static bool DisableBindlessTextures { get; set; }

    /// <summary>
    /// Indicates whether textures are passed to shaders as bindless handles packed into a constant buffer
    /// rather than bound to texture units. Requires <c>GL_ARB_bindless_texture</c>, which Intel does not
    /// implement on Windows; the slot bound path is kept for those drivers.
    /// </summary>
    /// <remarks>
    /// Latched on first read. Shader source, the buffer layouts and the renderer's binding paths all have to
    /// agree on this, and shaders start being preprocessed on a background thread that can get there before
    /// <see cref="Initialize"/> has queried the context.
    /// </remarks>
    public static bool BindlessTextures => bindlessTextures ??= bindlessTexturesSupported;

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

        var vendor = GL.GetString(StringName.Vendor);
        var renderer = GL.GetString(StringName.Renderer);
        var gpu = $"GPU: {renderer}, Driver: {GL.GetString(StringName.Version)}";

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

        // not supported on Intel integrated drivers
        IndirectCountSupported = vendor != "Intel";
        SlowMultiDrawIndirect = vendor == "Intel"
            && (renderer.Contains("Intel(R) HD", StringComparison.Ordinal) || renderer.Contains("Intel(R) UHD", StringComparison.Ordinal));

        bindlessTexturesSupported = extensions.Contains("GL_ARB_bindless_texture") && !DisableBindlessTextures;

        if (!bindlessTexturesSupported)
        {
            logger.LogWarning("Bindless textures are off, textures will be bound to texture units");
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
    /// <param name="rendererContext">The renderer context owning the state tracker for this GL context.</param>
    public static void SetDefaultRenderState(RendererContext rendererContext)
    {
        GL.Enable(EnableCap.TextureCubeMapSeamless);
        GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne); // reverse-Z clip range

        rendererContext.RenderState.SetPassBaseline(RenderState.Default);
        rendererContext.RenderState.ApplyDynamic(DynamicState.Default);

        EnableParallelShaderCompile();
    }

    /// <summary>
    /// Allows the driver to compile and link shaders on its own worker threads.
    /// </summary>
    public static void EnableParallelShaderCompile()
    {
        // Process-global driver setting; configure exactly once (re-issuing it mid-compile crashes some drivers).
        if (Interlocked.CompareExchange(ref parallelShaderCompileConfigured, 1, 0) == 0)
        {
            if (ParallelShaderCompileSupport == ParallelShaderCompileType.Khr)
            {
                GL.Khr.MaxShaderCompilerThreads(uint.MaxValue);
            }
            else if (ParallelShaderCompileSupport == ParallelShaderCompileType.Arb)
            {
                GL.Arb.MaxShaderCompilerThreads(uint.MaxValue);
            }
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
    /// Converts a <see cref="Matrix4x4"/> to an OpenTK Matrix3x4, transposing the matrix and dropping the last (M14/M24/M34/M44) column.
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
