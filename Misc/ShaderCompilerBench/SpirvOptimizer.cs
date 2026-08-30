using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ShaderCompilerBench;

/// <summary>
/// Runs spirv-opt over a module through <c>slang-glslang</c>, the library Slang uses for the same
/// job, so glslang's output can be optimized the way Slang's own SPIR-V is. glslang's built-in
/// optimizer cannot do this for OpenGL targets: it maps them to a SPIR-V 1.0 tools environment and
/// rejects the 1.3+ instructions it just emitted. This one works in a universal 1.5 environment.
/// </summary>
internal static unsafe partial class SpirvOptimizer
{
    private const string Library = "slang-glslang";

    /// <summary>GLSLANG_ACTION_OPTIMIZE_SPIRV.</summary>
    private const uint ActionOptimize = 2;

    /// <summary>Slang's optimization levels, which decide the pass list.</summary>
    public enum Level : uint
    {
        None = 0,
        Default = 1,
        High = 2,
        Maximal = 3,
    }

    /// <summary>The level to optimize at, or <see langword="null"/> to leave modules alone.</summary>
    public static Level? Requested { get; set; }

    public static string Describe()
        => Requested is Level level
            ? $"spirv-opt from slang-glslang over glslang's output, Slang level '{level.ToString().ToLowerInvariant()}'"
            : "spirv-opt: off, the driver gets glslang's literal translation";

    /// <summary>
    /// glslang_CompileRequest_1_3. The field order and widths have to match the header exactly,
    /// including the three ints of version with the padding the following pointer forces.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CompileRequest
    {
        public nuint SizeInBytes;
        public nint SourcePath;
        public nint InputBegin;
        public nint InputEnd;
        public nint DiagnosticFunc;
        public nint DiagnosticUserData;
        public nint OutputFunc;
        public nint OutputUserData;
        public int SlangStage;
        public uint Action;
        public uint OptimizationLevel;
        public uint DebugInfoType;
        public nint SpirvTargetName;
        public int SpirvMajor;
        public int SpirvMinor;
        public int SpirvPatch;
        public nint EntryPointName;
        public nint OptimizationFlags;
        public nuint OptimizationFlagCount;
    }

    [LibraryImport(Library)]
    private static partial int glslang_compile_1_3(ref CompileRequest request);

    /// <summary>What one call hands back through its callbacks.</summary>
    private sealed class Result
    {
        public byte[]? Output;
        public StringBuilder Diagnostics { get; } = new();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReceiveOutput(void* data, nuint size, void* userData)
    {
        var result = (Result)GCHandle.FromIntPtr((nint)userData).Target!;
        result.Output = new ReadOnlySpan<byte>(data, (int)size).ToArray();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ReceiveDiagnostic(void* data, nuint size, void* userData)
    {
        var result = (Result)GCHandle.FromIntPtr((nint)userData).Target!;
        result.Diagnostics.AppendLine(Encoding.UTF8.GetString((byte*)data, (int)size));
    }

    /// <summary>Optimizes both stages if a level was requested, timing each as a compiler stage.</summary>
    public static SpirvPair Apply(Timings timings, SpirvPair spirv)
    {
        if (Requested is not Level level)
        {
            return spirv;
        }

        var vertex = timings.Measure("spirv-opt: optimize (vertex)", () => Optimize(spirv.Vertex, level));
        var fragment = timings.Measure("spirv-opt: optimize (fragment)", () => Optimize(spirv.Fragment, level));
        return new SpirvPair(vertex, fragment);
    }

    private static byte[] Optimize(byte[] spirv, Level level)
    {
        var result = new Result();
        var handle = GCHandle.Alloc(result);

        try
        {
            fixed (byte* input = spirv)
            {
                var request = new CompileRequest
                {
                    SizeInBytes = (nuint)sizeof(CompileRequest),
                    InputBegin = (nint)input,
                    InputEnd = (nint)(input + spirv.Length),
                    DiagnosticFunc = (nint)(delegate* unmanaged[Cdecl]<void*, nuint, void*, void>)&ReceiveDiagnostic,
                    DiagnosticUserData = GCHandle.ToIntPtr(handle),
                    OutputFunc = (nint)(delegate* unmanaged[Cdecl]<void*, nuint, void*, void>)&ReceiveOutput,
                    OutputUserData = GCHandle.ToIntPtr(handle),
                    Action = ActionOptimize,
                    OptimizationLevel = (uint)level,
                };

                if (glslang_compile_1_3(ref request) != 0 || result.Output == null)
                {
                    throw new InvalidOperationException($"spirv-opt failed:\n{result.Diagnostics}".TrimEnd());
                }
            }
        }
        finally
        {
            handle.Free();
        }

        return result.Output;
    }
}
