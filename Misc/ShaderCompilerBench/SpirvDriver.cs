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
            throw new InvalidOperationException(
                $"{e.Message}\nThe modules were SPIR-V {SpirvPair.Version(spirv.Vertex)}, "
                + $"{spirv.Vertex.Length} bytes of vertex and {spirv.Fragment.Length} bytes of fragment.", e);
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
