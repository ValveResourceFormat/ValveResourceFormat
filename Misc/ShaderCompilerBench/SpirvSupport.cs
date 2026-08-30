using System.Globalization;
using OpenTK.Graphics.OpenGL;

namespace ShaderCompilerBench;

/// <summary>
/// Finds out what a driver will actually accept, by handing it SPIR-V rather than by asking. OpenGL
/// has no query for the highest SPIR-V version a driver takes: <c>GL_ARB_gl_spirv</c> is specified
/// against SPIR-V 1.0, and anything newer works only because a driver chose to be lenient. Run this
/// on every vendor the renderer has to support before relying on a version above 1.0.
/// </summary>
internal static class SpirvSupport
{
    private const int SpirVExtensions = 0x9553;
    private const int NumSpirVExtensions = 0x9554;

    /// <summary>A shader that uses nothing, to test the version gate on its own.</summary>
    private const string PlainVertex = """
        #version 460
        void main() { gl_Position = vec4(float(gl_VertexID), 0.0, 0.0, 1.0); }
        """;

    private const string PlainFragment = """
        #version 460
        layout(location = 0) out vec4 outColor;
        void main() { outColor = vec4(1.0); }
        """;

    /// <summary>
    /// A shader that discards. SPIR-V 1.6 deprecated <c>OpKill</c>, so glslang emits
    /// <c>OpTerminateInvocation</c> instead once the target reaches it, and a driver can take a
    /// trivial 1.6 module while rejecting that opcode. Version support has to be probed with
    /// something a real shader would contain, not with an empty one.
    /// </summary>
    private const string DiscardFragment = """
        #version 460
        layout(location = 0) out vec4 outColor;
        void main()
        {
            if (gl_FragCoord.x > 1e9) { discard; }
            outColor = vec4(gl_FragCoord.y);
        }
        """;

    /// <summary>
    /// Subgroup arithmetic, which the renderer's tiled light culling uses. Its SPIR-V capabilities
    /// were added in SPIR-V 1.3 and never had an extension, so a driver advertising
    /// <c>GL_KHR_shader_subgroup</c> alongside <c>GL_ARB_gl_spirv</c> has to take 1.3 to be useful.
    /// </summary>
    private const string SubgroupFragment = """
        #version 460
        #extension GL_KHR_shader_subgroup_arithmetic : require
        layout(location = 0) out vec4 outColor;
        void main() { outColor = vec4(subgroupAdd(gl_FragCoord.x)); }
        """;

    /// <summary>Newest first, because detection takes the first one the driver accepts.</summary>
    private static readonly string[] Versions = ["1.6", "1.5", "1.4", "1.3", "1.0"];

    private static string? detected;

    /// <summary>
    /// The newest SPIR-V this driver will actually take, found by handing it a module at each
    /// version until one links. There is no query for this, and every driver answers differently:
    /// OpenGL only promises 1.0, NVIDIA and Intel both go to 1.6.
    /// </summary>
    public static string HighestAccepted()
    {
        if (detected != null)
        {
            return detected;
        }

        foreach (var version in Versions)
        {
            if (Accepts(version, PlainFragment) && Accepts(version, DiscardFragment))
            {
                return detected = version;
            }
        }

        // Nothing linked, so leave the caller with what OpenGL guarantees and let it report the
        // real error rather than swallowing it here.
        return detected = "1.0";
    }

    public static int Run()
    {
        Console.WriteLine($"Vendor       {GL.GetString(StringName.Vendor)}");
        Console.WriteLine($"Renderer     {GL.GetString(StringName.Renderer)}");
        Console.WriteLine($"GL version   {GL.GetString(StringName.Version)}");
        Console.WriteLine($"GLSL version {GL.GetString(StringName.ShadingLanguageVersion)}");
        Console.WriteLine();

        var extensions = GlExtensions();
        Console.WriteLine($"GL_ARB_gl_spirv          {Yes(extensions.Contains("GL_ARB_gl_spirv"))}");
        Console.WriteLine($"GL_ARB_spirv_extensions  {Yes(extensions.Contains("GL_ARB_spirv_extensions"))}");
        Console.WriteLine($"GL_KHR_shader_subgroup   {Yes(extensions.Contains("GL_KHR_shader_subgroup"))}");
        Console.WriteLine();

        GL.GetInteger((GetPName)NumSpirVExtensions, out var spirvExtensionCount);
        Console.WriteLine($"SPIR-V extensions advertised ({spirvExtensionCount}):");

        for (var i = 0; i < spirvExtensionCount; i++)
        {
            Console.WriteLine("  " + GL.GetString((StringNameIndexed)SpirVExtensions, i));
        }

        Console.WriteLine();

        if (Glslang.Initialize() is string unavailable)
        {
            Console.Error.WriteLine(unavailable);
            return 1;
        }

        Console.WriteLine($"Compiling with glslang from {Glslang.Source}, then asking the driver to take the result:");
        Console.WriteLine();
        Console.WriteLine($"  {"SPIR-V",-8} {"plain",-26} {"discard",-26} {"subgroup arithmetic",-26}");
        Console.WriteLine($"  {new string('-', 8)} {new string('-', 26)} {new string('-', 26)} {new string('-', 26)}");

        foreach (var version in Versions.Reverse())
        {
            Console.WriteLine($"  {version,-8} {Attempt(version, PlainFragment),-26} {Attempt(version, DiscardFragment),-26} {Attempt(version, SubgroupFragment),-26}");
        }

        Console.WriteLine();
        Console.WriteLine($"Highest accepted: SPIR-V {HighestAccepted()}, which is what --spirv auto picks.");
        return 0;
    }

    private static bool Accepts(string version, string fragment)
        => Attempt(version, fragment).StartsWith("accepted", StringComparison.Ordinal);

    private static string Yes(bool value) => value ? "yes" : "no";

    private static HashSet<string> GlExtensions()
    {
        GL.GetInteger(GetPName.NumExtensions, out var count);
        var names = new HashSet<string>(count, StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            names.Add(GL.GetString(StringNameIndexed.Extensions, i));
        }

        return names;
    }

    /// <summary>
    /// Compiles and links one pair at the given version, returning what happened rather than
    /// throwing, because a rejection is the answer this is looking for.
    /// </summary>
    private static string Attempt(string version, string fragment)
    {
        var timings = new Timings("probe");
        SpirvPair spirv;

        try
        {
            Glslang.TargetSpirvVersion = Glslang.PackSpirvVersion(version);
            spirv = Glslang.Compile(timings, new SourcePair("probe", PlainVertex, fragment), GlslOrigin.ForTheDriver);
        }
        catch (InvalidOperationException e)
        {
            return "glslang: " + FirstLine(e.Message);
        }

        try
        {
            SpirvDriver.Run(timings, spirv, SlangPath.SpirvEntryPoint);
        }
        catch (InvalidOperationException e)
        {
            return "driver: " + FirstLine(e.Message);
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"accepted, {spirv.Vertex.Length + spirv.Fragment.Length} b");
    }

    private static string FirstLine(string message)
    {
        var line = message
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(candidate => candidate.Contains("ERROR", StringComparison.Ordinal))
            ?? message.Split('\n')[0];

        return line.Length > 20 ? line[..20] : line;
    }
}
