using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Materials;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// OpenGL environment initialization and default render state configuration.
/// </summary>
public static class GLEnvironment
{
    private const int VersionMajor = 4;
    private const int VersionMinor = 6;

    /// <summary>
    /// Minimum required OpenGL version.
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
    /// Indicates whether the driver can run mesh shaders, which means <c>GL_NV_mesh_shader</c>. It is the
    /// only mesh shader extension the GL bindings carry a draw entry point for.
    /// </summary>
    public static bool MeshShaderSupported { get; private set; }

    /// <summary>The GLSL extension the mesh shader stage is written against.</summary>
    public const string MeshShaderExtension = "GL_NV_mesh_shader";

    /// <summary>
    /// Most vertices one mesh shader workgroup may emit, or 0 when mesh shaders are unsupported.
    /// </summary>
    public static int MaxMeshOutputVertices { get; private set; }

    /// <summary>
    /// Most primitives one mesh shader workgroup may emit, or 0 when mesh shaders are unsupported.
    /// </summary>
    public static int MaxMeshOutputPrimitives { get; private set; }

    /// <summary>
    /// Most workgroups one mesh shader draw may dispatch, or 0 when mesh shaders are unsupported.
    /// </summary>
    public static int MaxDrawMeshTasks { get; private set; }

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
        IndirectCountSupported = extensions.Contains("GL_ARB_indirect_parameters") && vendor != "Intel";
        SlowMultiDrawIndirect = (vendor == "Intel"
            && (renderer.Contains("Intel(R) HD", StringComparison.Ordinal) || renderer.Contains("Intel(R) UHD", StringComparison.Ordinal)))
            || renderer.Contains("llvmpipe", StringComparison.Ordinal);

        if (extensions.Contains(MeshShaderExtension))
        {
            MaxMeshOutputVertices = GetMeshShaderLimit(NvMeshShader.MaxMeshOutputVerticesNv, 256);
            MaxMeshOutputPrimitives = GetMeshShaderLimit(NvMeshShader.MaxMeshOutputPrimitivesNv, 256);
            MaxDrawMeshTasks = GetMeshShaderLimit(NvMeshShader.MaxDrawMeshTasksCountNv, 65535);

            MeshShaderSupported = MaxMeshOutputVertices >= MeshletLimits.MaxVertices
                && MaxMeshOutputPrimitives >= MeshletLimits.MaxPrimitives;

            logger.LogDebug("{Extension}: {Vertices} vertices and {Primitives} primitives per meshlet, {Tasks} workgroups per draw",
                MeshShaderExtension, MaxMeshOutputVertices, MaxMeshOutputPrimitives, MaxDrawMeshTasks);
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
    /// Reads one mesh shader limit. The extension guarantees a floor for each of these, so a driver that
    /// answers the query with an error rather than a number (some do) reports the guaranteed value instead
    /// of leaving the limit at zero, which would read as a device far below what the extension promises.
    /// </summary>
    private static int GetMeshShaderLimit(NvMeshShader parameter, int guaranteed)
    {
        var value = GL.GetInteger((GetPName)parameter);

        // Also clears the flag, so an unanswered query does not surface later as a stray error
        var error = GL.GetError();

        return error == ErrorCode.NoError && value > 0 ? value : guaranteed;
    }

    /// <summary>
    /// Sets the default OpenGL render state for Source 2 rendering.
    /// </summary>
    public static void SetDefaultRenderState()
    {
        GL.Enable(EnableCap.TextureCubeMapSeamless);
        GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne); // reverse-Z clip range

        GraphicsContext.RenderState.SetPassBaseline(RenderState.Default);
        GraphicsContext.RenderState.ApplyDynamic(DynamicState.Default);

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
