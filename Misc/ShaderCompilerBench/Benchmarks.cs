using System.Globalization;

namespace ShaderCompilerBench;

/// <param name="SaltBase">
/// Base value stamped into every source so no compile in this run can be served out of a driver
/// or compiler cache. It has to differ between runs as well as between iterations, because the
/// NVIDIA shader cache lives on disk and outlives the process.
/// </param>
internal sealed record BenchmarkOptions(int Iterations, int Warmup, bool Dump, IReadOnlyList<(string Name, string Value)> Macros, string RendererShader, int SaltBase, string SpirvVersion)
{
    /// <summary>The value to stamp into the source for one iteration, warmup iterations included.</summary>
    public int Salt(int iteration) => SaltBase + iteration;
}

internal sealed record Benchmark(string Name, Func<BenchmarkOptions, Timings> Body);

internal static class Benchmarks
{
    /// <summary>The Slang module every Slang path compiles, loaded from the Shaders folder.</summary>
    private const string BenchModule = "bench";

    public static IReadOnlyList<Benchmark> All { get; } =
    [
        // What the renderer does today.
        new("GLSL -> driver: renderer shader", options =>
            RunGlsl($"GLSL -> driver: renderer {options.RendererShader}", Sources.FromRenderer(options.RendererShader), options)),

        // The same sources, compiled ahead of time into SPIR-V instead.
        new("GLSL -> glslang -> SPIR-V -> driver: renderer shader", options =>
            RunGlslang($"GLSL -> glslang -> SPIR-V -> driver: renderer {options.RendererShader}", Sources.FromRenderer(options.RendererShader), options)),

        new("Slang -> SPIR-V -> driver: bench", options =>
            SlangPath.Run("Slang -> SPIR-V -> driver: bench", SlangOutput.Spirv, BenchModule, options, "spirv_1_3")),

        new("Slang -> GLSL -> driver: bench", options =>
            SlangPath.Run("Slang -> GLSL -> driver: bench", SlangOutput.Glsl, BenchModule, options, "glsl_460")),

        new("Slang -> GLSL -> glslang -> SPIR-V -> driver: bench", options =>
            SlangPath.Run("Slang -> GLSL -> glslang -> SPIR-V -> driver: bench", SlangOutput.GlslThenGlslang, BenchModule, options, "glsl_460")),
    ];

    private static Timings RunGlsl(string title, SourcePair sources, BenchmarkOptions options)
    {
        var timings = new Timings(title);
        Describe(timings, sources, options.Dump);

        for (var i = 0; i < options.Warmup + options.Iterations; i++)
        {
            var isWarmup = i < options.Warmup;
            var target = isWarmup ? new Timings(title) : timings;

            GlslPath.Run(target, sources, options.Salt(i));
        }

        return timings;
    }

    private static Timings RunGlslang(string title, SourcePair sources, BenchmarkOptions options)
    {
        var timings = new Timings(title);

        if (Glslang.Initialize() is string unavailable)
        {
            timings.Skipped = unavailable;
            return timings;
        }

        var version = Glslang.Resolve(options.SpirvVersion);
        timings.Notes.Add($"glslang from {Glslang.Source}, targeting SPIR-V {version}");
        timings.Notes.Add(Glslang.DescribeBindings());
        Describe(timings, sources, options.Dump);

        SpirvPair? last = null;

        for (var i = 0; i < options.Warmup + options.Iterations; i++)
        {
            var isWarmup = i < options.Warmup;
            var target = isWarmup ? new Timings(title) : timings;
            var salt = options.Salt(i);

            try
            {
                // The renderer's GLSL declares loose uniforms, which OpenGL SPIR-V has no place for,
                // so glslang is told to gather them into a real block.
                last = Glslang.Compile(target, Salt(sources, salt), GlslOrigin.ForTheDriver);
            }
            catch (InvalidOperationException e)
            {
                // A shader the driver accepts is not automatically one glslang accepts, and that
                // answer is worth reporting rather than crashing on.
                timings.Skipped = e.Message;
                return timings;
            }

            SpirvDriver.Run(target, last, SlangPath.SpirvEntryPoint);
        }

        timings.Notes.Add(Glslang.Describe(last!));

        if (options.Dump)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "dump");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"{sources.Name}.glslang.vert.spv"), last!.Vertex);
            File.WriteAllBytes(Path.Combine(directory, $"{sources.Name}.glslang.frag.spv"), last.Fragment);
        }

        return timings;
    }

    private static SourcePair Salt(SourcePair sources, int salt)
        => sources with { Vertex = GlslPath.SaltForSpirv(sources.Vertex, salt), Fragment = GlslPath.SaltForSpirv(sources.Fragment, salt) };

    private static void Describe(Timings timings, SourcePair sources, bool dump)
    {
        timings.Notes.Add(string.Create(CultureInfo.InvariantCulture,
            $"source: {sources.Name}, {sources.Lines} lines, {sources.Bytes / 1024.0:F1} KiB after preprocessing"));

        if (!dump)
        {
            return;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "dump");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{sources.Name}.vert.glsl"), sources.Vertex);
        File.WriteAllText(Path.Combine(directory, $"{sources.Name}.frag.glsl"), sources.Fragment);
        timings.Notes.Add("dumped to " + directory);
    }
}
