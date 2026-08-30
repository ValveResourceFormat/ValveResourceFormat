using System.Globalization;
using System.Text;
using OpenTK.Graphics.OpenGL;

namespace ShaderCompilerBench;

/// <summary>A vertex/fragment pair of SPIR-V modules, whatever produced them.</summary>
internal sealed record SpirvPair(byte[] Vertex, byte[] Fragment)
{
    /// <summary>Reads the version out of word one of the SPIR-V header.</summary>
    public static string Version(byte[] spirv)
    {
        if (spirv.Length < 8)
        {
            return "?";
        }

        var version = BitConverter.ToUInt32(spirv, 4);
        return $"{(version >> 16) & 0xFF}.{(version >> 8) & 0xFF}";
    }
}

/// <summary>
/// The half of a SPIR-V pipeline that belongs to the driver: upload the binaries, specialize them
/// into shader objects, and link the program.
/// </summary>
internal static class SpirvDriver
{
    /// <summary>GL_SHADER_BINARY_FORMAT_SPIR_V, the only shader binary format OpenGL accepts.</summary>
    private const int ShaderBinaryFormatSpirV = 0x9551;

    public static void Run(Timings timings, SpirvPair spirv, string entryPoint)
    {
        try
        {
            Compile(timings, spirv, entryPoint);
            WarnAboutInterface(timings, spirv);
        }
        catch (InvalidOperationException e)
        {
            throw new InvalidOperationException($"{e.Message}\n{Diagnose(spirv, entryPoint)}", e);
        }
    }

    /// <summary>
    /// Works out what to say about a module the driver would not take. Which stage fails narrows it
    /// a long way, and what the module declares against what the driver advertises is the only
    /// comparison available when the info log is empty.
    /// </summary>
    private static string Diagnose(SpirvPair spirv, string entryPoint)
    {
        var report = new StringBuilder();

        report.Append(CultureInfo.InvariantCulture,
            $"The modules were SPIR-V {SpirvPair.Version(spirv.Vertex)}, {spirv.Vertex.Length} bytes of vertex and {spirv.Fragment.Length} bytes of fragment.");

        report.Append(CultureInfo.InvariantCulture, $"\n  vertex alone:   {LinkAlone(ShaderType.VertexShader, spirv.Vertex, entryPoint)}");
        report.Append(CultureInfo.InvariantCulture, $"\n  fragment alone: {LinkAlone(ShaderType.FragmentShader, spirv.Fragment, entryPoint)}");

        var advertised = AdvertisedExtensions();
        report.Append(CultureInfo.InvariantCulture, $"\n  driver advertises {advertised.Count} SPIR-V extensions: {string.Join(", ", advertised)}");

        var vertex = SpirvInfo.Read(spirv.Vertex);
        var fragment = SpirvInfo.Read(spirv.Fragment);

        Describe(report, "vertex", vertex, advertised);
        Describe(report, "fragment", fragment, advertised);
        MatchInterface(report, vertex, fragment);

        return report.ToString();
    }

    /// <summary>
    /// Reports an unmatched fragment input even when the driver took the program, because the
    /// drivers that reject it say nothing useful and the ones that accept it hide the problem.
    /// </summary>
    private static void WarnAboutInterface(Timings timings, SpirvPair spirv)
    {
        var report = new StringBuilder();
        MatchInterface(report, SpirvInfo.Read(spirv.Vertex), SpirvInfo.Read(spirv.Fragment));

        var text = report.ToString();

        if (!text.Contains("no vertex output", StringComparison.Ordinal))
        {
            return;
        }

        var note = "warning:" + text.ReplaceLineEndings(" ").Replace("  ", " ", StringComparison.Ordinal);

        if (!timings.Notes.Contains(note, StringComparer.Ordinal))
        {
            timings.Notes.Add(note);
        }
    }

    /// <summary>
    /// Stages are matched by location, not by name, and a fragment input with no vertex output
    /// behind it is a link error that several drivers report with an empty log. GLSL front ends drop
    /// an unread input before it gets that far, so this only shows up once the shader is going
    /// through SPIR-V.
    /// </summary>
    private static void MatchInterface(StringBuilder report, SpirvInfo.Declarations vertex, SpirvInfo.Declarations fragment)
    {
        var produced = vertex.Outputs.Select(output => output.Location).ToHashSet();
        var orphans = fragment.Inputs.Where(input => !produced.Contains(input.Location)).ToArray();

        if (orphans.Length == 0)
        {
            report.Append("\n  stage interface: every fragment input has a vertex output behind it");
            return;
        }

        report.Append(CultureInfo.InvariantCulture,
            $"\n  stage interface: {orphans.Length} fragment input(s) with no vertex output, which is a link error:");

        foreach (var orphan in orphans)
        {
            report.Append(CultureInfo.InvariantCulture, $"\n    {orphan.Name} at location {orphan.Location}");
        }
    }

    private static void Describe(StringBuilder report, string stage, SpirvInfo.Declarations declarations, HashSet<string> advertised)
    {
        report.Append(CultureInfo.InvariantCulture, $"\n  {stage} capabilities: {string.Join(", ", declarations.Capabilities)}");

        var required = declarations.Extensions
            .Select(extension => advertised.Contains(extension) ? extension : extension + " (NOT ADVERTISED)")
            .ToArray();

        report.Append(CultureInfo.InvariantCulture,
            $"\n  {stage} extensions:   {(required.Length == 0 ? "none" : string.Join(", ", required))}");
    }

    private static HashSet<string> AdvertisedExtensions()
    {
        GL.GetInteger((GetPName)0x9554 /* GL_NUM_SPIR_V_EXTENSIONS */, out var count);
        var names = new HashSet<string>(count, StringComparer.Ordinal);

        for (var i = 0; i < count; i++)
        {
            names.Add(GL.GetString((StringNameIndexed)0x9553 /* GL_SPIR_V_EXTENSIONS */, i));
        }

        return names;
    }

    /// <summary>Links one stage on its own, which tells us which half the driver objects to.</summary>
    private static string LinkAlone(ShaderType type, byte[] module, string entryPoint)
    {
        var shader = GL.CreateShader(type);
        var program = GL.CreateProgram();

        try
        {
            LoadBinary(shader, module);
            Specialize(shader, entryPoint);
            GlslPath.CheckShader(shader, type.ToString());

            GL.AttachShader(program, shader);
            GL.LinkProgram(program);
            GlslPath.CheckProgram(program);

            return "links";
        }
        catch (InvalidOperationException e)
        {
            return e.Message.ReplaceLineEndings(" ");
        }
        finally
        {
            GL.DeleteProgram(program);
            GL.DeleteShader(shader);
        }
    }

    private static void Compile(Timings timings, SpirvPair spirv, string entryPoint)
    {
        var vertex = GL.CreateShader(ShaderType.VertexShader);
        var fragment = GL.CreateShader(ShaderType.FragmentShader);
        var program = GL.CreateProgram();

        try
        {
            timings.Measure("glShaderBinary (both stages)", () =>
            {
                LoadBinary(vertex, spirv.Vertex);
                LoadBinary(fragment, spirv.Fragment);
            });

            timings.Measure("glSpecializeShader (vertex)", () => Specialize(vertex, entryPoint));
            timings.Measure("glSpecializeShader (fragment)", () => Specialize(fragment, entryPoint));

            timings.Measure("COMPILE_STATUS wait (vertex)", () => GlslPath.CheckShader(vertex, "vertex"));
            timings.Measure("COMPILE_STATUS wait (fragment)", () => GlslPath.CheckShader(fragment, "fragment"));

            timings.Measure("glLinkProgram", () =>
            {
                GL.AttachShader(program, vertex);
                GL.AttachShader(program, fragment);
                GL.LinkProgram(program);
            });

            timings.Measure("LINK_STATUS wait", () => GlslPath.CheckProgram(program));
            GlslPath.ReadProgramBinary(timings, program);
            DrawProbe.Run(timings, program);
        }
        finally
        {
            GL.DeleteProgram(program);
            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
        }
    }

    private static unsafe void LoadBinary(int shader, byte[] spirv)
    {
        fixed (byte* data = spirv)
        {
            GL.ShaderBinary(1, ref shader, (ShaderBinaryFormat)ShaderBinaryFormatSpirV, (nint)data, spirv.Length);
        }
    }

    private static unsafe void Specialize(int shader, string entryPoint)
    {
        GL.SpecializeShader((uint)shader, entryPoint, 0, (uint*)null, (uint*)null);
    }
}
