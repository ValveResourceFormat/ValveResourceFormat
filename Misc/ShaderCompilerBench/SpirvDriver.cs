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

        Describe(report, "vertex", spirv.Vertex, advertised);
        Describe(report, "fragment", spirv.Fragment, advertised);

        return report.ToString();
    }

    private static void Describe(StringBuilder report, string stage, byte[] module, HashSet<string> advertised)
    {
        var declarations = SpirvInfo.Read(module);

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
