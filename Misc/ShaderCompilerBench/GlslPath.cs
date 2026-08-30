using System.Globalization;
using OpenTK.Graphics.OpenGL;

namespace ShaderCompilerBench;

/// <summary>
/// The path the renderer uses today: hand GLSL text to the driver and let its own front end compile it.
/// </summary>
internal static class GlslPath
{
    /// <summary>
    /// Compiles and links one vertex/fragment pair, timing the driver calls separately from the
    /// status queries that block until the driver's worker threads are actually done.
    /// </summary>
    /// <param name="salt">Value to stamp into the source so the driver cannot reuse a cached compile,
    /// or <see langword="null"/> when the source already carries one.</param>
    public static void Run(Timings timings, SourcePair sources, int? salt)
    {
        var vertexSource = salt is int vertexSalt ? Salt(sources.Vertex, vertexSalt) : sources.Vertex;
        var fragmentSource = salt is int fragmentSalt ? Salt(sources.Fragment, fragmentSalt) : sources.Fragment;

        var vertex = GL.CreateShader(ShaderType.VertexShader);
        var fragment = GL.CreateShader(ShaderType.FragmentShader);
        var program = GL.CreateProgram();

        try
        {
            timings.Measure("glCompileShader (vertex)", () =>
            {
                GL.ShaderSource(vertex, vertexSource);
                GL.CompileShader(vertex);
            });

            timings.Measure("glCompileShader (fragment)", () =>
            {
                GL.ShaderSource(fragment, fragmentSource);
                GL.CompileShader(fragment);
            });

            timings.Measure("COMPILE_STATUS wait (vertex)", () => CheckShader(vertex, "vertex"));
            timings.Measure("COMPILE_STATUS wait (fragment)", () => CheckShader(fragment, "fragment"));

            timings.Measure("glLinkProgram", () =>
            {
                GL.AttachShader(program, vertex);
                GL.AttachShader(program, fragment);
                GL.LinkProgram(program);
            });

            timings.Measure("LINK_STATUS wait", () => CheckProgram(program));
            ReadProgramBinary(timings, program);
            DrawProbe.Run(timings, program);
        }
        finally
        {
            GL.DeleteProgram(program);
            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
        }
    }

    /// <summary>
    /// Drivers key their compiled shader cache on the source text, so every iteration has to look new
    /// or all but the first would be measuring a cache lookup. The define lands right after
    /// <c>#version</c>, which has to stay on the first line.
    /// </summary>
    public static string Salt(string source, int salt)
    {
        var firstLineEnd = source.IndexOf('\n', StringComparison.Ordinal) + 1;
        return string.Create(CultureInfo.InvariantCulture,
            $"{source[..firstLineEnd]}#define BENCH_SALT {salt}\n{source[firstLineEnd..]}");
    }

    public static void CheckShader(int shader, string stage)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var status);

        if (status != 1)
        {
            GL.GetShaderInfoLog(shader, out var log);
            throw new InvalidOperationException($"Failed to compile {stage} shader{Explain(log)}");
        }
    }

    /// <summary>
    /// Drivers are allowed to fail with nothing to say, and several do when handed SPIR-V they
    /// dislike. Saying so beats printing a bare colon, and the GL error code is sometimes the only
    /// thing left to go on.
    /// </summary>
    private static string Explain(string log)
    {
        var error = GL.GetError();
        var errorText = error == ErrorCode.NoError ? string.Empty : $", glGetError {error}";

        return string.IsNullOrWhiteSpace(log)
            ? $". The driver returned an empty info log{errorText}. {Advice}"
            : $"{errorText}:\n{log}";
    }

    private const string Advice = "Try --spirv 1.0 and --bindings overlapping, and --dump to write out the module.";

    /// <summary>
    /// Asks the driver for the compiled program, which is how a shader cache would save it, and
    /// which forces any code generation the driver was still holding back after the link. Its size
    /// is reported because it is the only evidence available that a suspiciously fast link really
    /// did produce code.
    /// </summary>
    public static void ReadProgramBinary(Timings timings, int program)
    {
        var length = 0;

        timings.Measure("glGetProgramBinary", () =>
        {
            GL.GetProgram(program, (GetProgramParameterName)0x8741 /* GL_PROGRAM_BINARY_LENGTH */, out length);

            if (length <= 0)
            {
                return;
            }

            var binary = new byte[length];
            GL.GetProgramBinary(program, length, out _, out BinaryFormat _, binary);
        });

        var note = string.Create(CultureInfo.InvariantCulture, $"driver program binary: {length} bytes");

        if (!timings.Notes.Contains(note, StringComparer.Ordinal))
        {
            timings.Notes.Add(note);
        }
    }

    public static void CheckProgram(int program)
    {
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var status);

        if (status != 1)
        {
            GL.GetProgramInfoLog(program, out var log);
            throw new InvalidOperationException($"Failed to link program{Explain(log)}");
        }
    }
}
