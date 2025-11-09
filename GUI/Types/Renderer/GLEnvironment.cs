using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;

namespace GUI.Types.Renderer;

static class GLEnvironment
{
    private const int VersionMajor = 4;
    private const int VersionMinor = 6;

    public static readonly Version RequiredVersion = new(VersionMajor, VersionMinor);

#if DEBUG
    public static ContextFlags Flags => ContextFlags.ForwardCompatible | ContextFlags.Debug;
#else
    public static ContextFlags Flags => ContextFlags.ForwardCompatible;
#endif

    public enum ParallelShaderCompileType : byte
    {
        None,
        Arb,
        Khr,
    }

    public static ParallelShaderCompileType ParallelShaderCompileSupport { get; private set; } = ParallelShaderCompileType.None;
    public static string? GpuRendererAndDriver { get; private set; }

    public static void Initialize()
    {
        if (GpuRendererAndDriver != null)
        {
            return;
        }

        var minor = GL.GetInteger(GetPName.MinorVersion);
        var major = GL.GetInteger(GetPName.MajorVersion);

        var gpu = $"GPU: {GL.GetString(StringName.Renderer)}, Driver: {GL.GetString(StringName.Version)}";

        GpuRendererAndDriver = gpu;

        Log.Debug("OpenGL", $"{gpu}, OS: {Environment.OSVersion}");

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
            Log.Warn("OpenGL", "Parallel shader compilation is not supported.");
        }
    }
}
