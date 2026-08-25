using System.Threading;
using Microsoft.Extensions.Logging;
using OpenTK;
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
    /// Indicates whether the driver can run mesh shaders.
    /// </summary>
    public static bool MeshShaderSupported { get; private set; }

    /// <summary>
    /// The GLSL extension the mesh shader stage is written against, empty when there is none. The vendor
    /// neutral <c>GL_EXT_mesh_shader</c> wins where the driver has it, which means resolving
    /// <c>glDrawMeshTasksEXT</c> by hand because the bindings only carry the NV entry points.
    /// </summary>
    public static string MeshShaderExtension { get; private set; } = string.Empty;

    /// <summary>
    /// The loader GL entry points are resolved through, for the ones the bindings do not declare. Set
    /// alongside <c>GL.LoadBindings</c>, and so before <see cref="Initialize"/> runs.
    /// </summary>
    public static IBindingsContext? BindingsContext { get; set; }

    private static unsafe delegate* unmanaged<uint, uint, uint, void> drawMeshTasksExt;

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

        var meshShaderNv = extensions.Contains("GL_NV_mesh_shader");

        if (extensions.Contains("GL_EXT_mesh_shader") && TryBindDrawMeshTasksExt())
        {
            MeshShaderExtension = "GL_EXT_mesh_shader";
        }
        else if (meshShaderNv)
        {
            MeshShaderExtension = "GL_NV_mesh_shader";
        }
        else if (extensions.Contains("GL_EXT_mesh_shader"))
        {
            logger.LogWarning("GL_EXT_mesh_shader is available but glDrawMeshTasksEXT did not resolve");
        }

        if (MeshShaderExtension.Length > 0)
        {
            // The EXT tokens alias the NV ones, so one query answers for either extension
            MaxMeshOutputVertices = GL.GetInteger((GetPName)NvMeshShader.MaxMeshOutputVerticesNv);
            MaxMeshOutputPrimitives = GL.GetInteger((GetPName)NvMeshShader.MaxMeshOutputPrimitivesNv);

            // EXT counts workgroups per dimension instead and has no equivalent of this one, so an EXT only
            // driver takes the smallest count the spec lets an implementation report
            MaxDrawMeshTasks = meshShaderNv ? GL.GetInteger((GetPName)NvMeshShader.MaxDrawMeshTasksCountNv) : 65535;

            MeshShaderSupported = MaxMeshOutputVertices >= MeshletLimits.MaxVertices
                && MaxMeshOutputPrimitives >= MeshletLimits.MaxPrimitives;

            if (!MeshShaderSupported)
            {
                logger.LogWarning("{Extension} caps out at {Vertices} vertices and {Primitives} primitives per meshlet, which is below what Source 2 meshlets need",
                    MeshShaderExtension, MaxMeshOutputVertices, MaxMeshOutputPrimitives);
            }
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

    private static unsafe bool TryBindDrawMeshTasksExt()
    {
        var address = BindingsContext?.GetProcAddress("glDrawMeshTasksEXT") ?? IntPtr.Zero;

        if (address == IntPtr.Zero)
        {
            return false;
        }

        drawMeshTasksExt = (delegate* unmanaged<uint, uint, uint, void>)address;
        return true;
    }

    /// <summary>
    /// Dispatches a mesh shader draw of <paramref name="groupCount"/> workgroups along x, through whichever
    /// extension <see cref="MeshShaderExtension"/> named. Both number <c>gl_WorkGroupID.x</c> from zero, so
    /// the shader does not care which one ran it.
    /// </summary>
    /// <param name="groupCount">Workgroups to dispatch, at most <see cref="MaxDrawMeshTasks"/>.</param>
    public static unsafe void DrawMeshTasks(uint groupCount)
    {
        if (drawMeshTasksExt != null)
        {
            drawMeshTasksExt(groupCount, 1u, 1u);
            return;
        }

        GL.NV.DrawMeshTask(0u, groupCount);
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
