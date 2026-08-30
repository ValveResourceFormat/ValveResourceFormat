using System.Diagnostics;
using System.Globalization;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using ValveResourceFormat.Renderer;

namespace ShaderCompilerBench;

/// <summary>
/// Times every way this renderer could get a shader into OpenGL: the driver's own GLSL front end,
/// which is what it does today, the same GLSL compiled ahead of time into SPIR-V by glslang, and
/// Slang emitting either SPIR-V or GLSL. Every stage of every path is measured, from the compiler
/// parsing its source to the driver reporting link status.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Prefix of the line a child process uses to hand its total back to the parent. Everything
    /// else a child prints is relayed verbatim.
    /// </summary>
    private const string ResultMarker = "##RESULT ";

    public static int Main(string[] args)
    {
        var iterations = 10;
        var warmup = 2;
        string? filter = null;
        var dump = false;
        var inProcess = false;
        var child = -1;
        // csgo_environment rather than complex: same size class, and it is the biggest renderer
        // shader that survives every path, so a default run has no holes in it.
        var rendererShader = "csgo_environment";
        var spirvVersion = Glslang.AutoVersion;
        var saltBase = -1;
        var probe = false;
        var list = false;
        var macros = new List<(string Name, string Value)>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-n" or "--iterations" when i + 1 < args.Length:
                    iterations = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "-w" or "--warmup" when i + 1 < args.Length:
                    warmup = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--salt-base" when i + 1 < args.Length:
                    saltBase = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--spirv" when i + 1 < args.Length:
                    spirvVersion = args[++i];
                    break;
                case "--shader" when i + 1 < args.Length:
                    rendererShader = args[++i];
                    break;
                case "--only" when i + 1 < args.Length:
                    filter = args[++i];
                    break;
                case "-D" when i + 1 < args.Length:
                    var definition = args[++i];
                    var equals = definition.IndexOf('=', StringComparison.Ordinal);
                    macros.Add(equals < 0 ? (definition, "1") : (definition[..equals], definition[(equals + 1)..]));
                    break;
                case "--list":
                    list = true;
                    break;
                case "--bindings" when i + 1 < args.Length:
                    Glslang.SeparateBindingSpaces = args[++i] switch
                    {
                        "separate" => true,
                        "overlapping" => false,
                        var value => throw new ArgumentException($"--bindings takes separate or overlapping, not '{value}'"),
                    };
                    break;
                case "--probe":
                    probe = true;
                    break;
                case "--dump":
                    dump = true;
                    break;
                case "--in-process":
                    inProcess = true;
                    break;
                case "--child" when i + 1 < args.Length:
                    child = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}'");
                    Console.Error.WriteLine("Usage: ShaderCompilerBench [-n iterations] [-w warmup] [--shader name] [--spirv auto|1.0] [--bindings separate|overlapping] [--only index] [-D NAME=VALUE] [--dump] [--in-process] [--probe] [--list]");
                    return 1;
            }
        }

        if (saltBase < 0)
        {
            saltBase = Random.Shared.Next(1, int.MaxValue / 2);
        }

        var options = new BenchmarkOptions(iterations, warmup, dump, macros, rendererShader, saltBase, spirvVersion);

        if (list)
        {
            List();
            return 0;
        }

        if (probe)
        {
            using var probeWindow = CreateContext();
            return SpirvSupport.Run();
        }

        if (child >= 0)
        {
            return RunChild(child, options);
        }

        // An index is accepted as well as a substring, because the path names overlap: "GLSL -> driver"
        // is a part of "Slang -> GLSL -> driver" too. --list prints the indices.
        var selected = Benchmarks.All
            .Index()
            .Where(entry => filter == null
                || (int.TryParse(filter, CultureInfo.InvariantCulture, out var index)
                    ? entry.Index == index
                    : entry.Item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (selected.Length == 0)
        {
            Console.Error.WriteLine($"No benchmark matches '{filter}'. Available:");

            foreach (var (index, benchmark) in Benchmarks.All.Index())
            {
                Console.Error.WriteLine($"  {index}  {benchmark.Name}");
            }

            return 1;
        }

        using (var window = CreateContext())
        {
            PrintEnvironment();
        }

        var results = new List<string>();

        foreach (var (index, benchmark) in selected)
        {
            Console.WriteLine($"Running {benchmark.Name}...");

            results.Add(inProcess
                ? RunInProcess(benchmark, options)
                : RunAsChildProcess(index, saltBase, args));
        }

        Summary(results);
        return 0;
    }

    /// <summary>Prints what can be passed to --only and to --shader.</summary>
    private static void List()
    {
        Console.WriteLine("Paths (--only takes an index, or any part of a name):");

        foreach (var (index, benchmark) in Benchmarks.All.Index())
        {
            Console.WriteLine($"  {index}  {benchmark.Name}");
        }

        Console.WriteLine();
        Console.WriteLine("Renderer shaders (--shader), those with both a vertex and a fragment stage:");

        foreach (var chunk in Sources.RendererShaders().Chunk(4))
        {
            Console.WriteLine("  " + string.Join(string.Empty, chunk.Select(name => name.PadRight(28))).TrimEnd());
        }
    }

    /// <summary>
    /// Runs one benchmark and prints its report. Benchmarks are run one to a process because the
    /// Slang wrapper corrupts memory when sessions for different targets are created in the same
    /// one, and because a fresh process gives every path the same cold driver state.
    /// </summary>
    private static int RunChild(int index, BenchmarkOptions options)
    {
        if (index >= Benchmarks.All.Count)
        {
            Console.Error.WriteLine($"No benchmark at index {index}");
            return 1;
        }

        var benchmark = Benchmarks.All[index];

        using var window = CreateContext();

        Timings timings;

        try
        {
            timings = benchmark.Body(options);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }

        timings.Report();

        if (benchmark.Name.Contains("Slang", StringComparison.Ordinal))
        {
            // Left until every session has been created, see SlangPath.BuildTag.
            Console.WriteLine($"  Slang version: {SlangPath.BuildTag()}");
        }

        // One line, because the parent scans the child's output line by line.
        Console.WriteLine($"{ResultMarker}{timings.Title}\t{Describe(timings)}");

        return 0;
    }

    /// <summary>The one line summary of a benchmark, as "title, tab, result".</summary>
    private static string Describe(Timings timings) => timings.Skipped != null
        ? "skipped: " + string.Join(" / ", timings.Skipped.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        : string.Create(CultureInfo.InvariantCulture,
            $"{timings.MedianTotal,10:F3} ms  ({timings.MedianCompilerTotal:F1} compiler + {timings.MedianDriverTotal:F1} driver)");

    private static string RunInProcess(Benchmark benchmark, BenchmarkOptions options)
    {
        try
        {
            var timings = benchmark.Body(options);
            timings.Report();
            return $"{timings.Title}\t{Describe(timings)}";
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return $"{benchmark.Name}\tfailed";
        }
    }

    private static string RunAsChildProcess(int index, int saltBase, string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath ?? "ShaderCompilerBench.exe")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        // The child is filtered by index and gets its salt base from the parent, so neither of
        // those arguments is passed along.
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--only" or "--salt-base")
            {
                i++;
                continue;
            }

            start.ArgumentList.Add(args[i]);
        }

        start.ArgumentList.Add("--child");
        start.ArgumentList.Add(index.ToString(CultureInfo.InvariantCulture));

        // Every child of one run shares the salt base, chosen once so a repeated run never asks
        // the driver to compile something its disk cache already holds.
        start.ArgumentList.Add("--salt-base");
        start.ArgumentList.Add(saltBase.ToString(CultureInfo.InvariantCulture));

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start the benchmark child process");

        var result = "\tfailed";

        while (process.StandardOutput.ReadLine() is string line)
        {
            if (line.StartsWith(ResultMarker, StringComparison.Ordinal))
            {
                result = line[ResultMarker.Length..];
                continue;
            }

            Console.WriteLine(line);
        }

        process.WaitForExit();

        if (process.ExitCode != 0 && result == "\tfailed")
        {
            result = $"\tfailed (exit {process.ExitCode})";
        }

        return result;
    }

    private static NativeWindow CreateContext()
    {
        var window = new NativeWindow(new()
        {
            APIVersion = GLEnvironment.RequiredVersion,
            Flags = ContextFlags.ForwardCompatible | ContextFlags.Offscreen,
            StartVisible = false,
            Title = "Source 2 Viewer Shader Compiler Bench"
        });

        window.MakeCurrent();
        return window;
    }

    private static void PrintEnvironment()
    {
        Console.WriteLine($"Vendor       {GL.GetString(StringName.Vendor)}");
        Console.WriteLine($"Renderer     {GL.GetString(StringName.Renderer)}");
        Console.WriteLine($"GL version   {GL.GetString(StringName.Version)}");
        Console.WriteLine($"GLSL version {GL.GetString(StringName.ShadingLanguageVersion)}");

        GL.GetInteger((GetPName)0x9554 /* GL_NUM_SPIR_V_EXTENSIONS */, out var spirvExtensions);
        Console.WriteLine($"SPIR-V       {spirvExtensions} SPIR-V extensions advertised");
        Console.WriteLine();
    }

    private static void Summary(List<string> results)
    {
        if (results.Count < 2)
        {
            return;
        }

        var rows = results.Select(result => result.Split('\t', 2)).ToArray();
        var width = rows.Max(row => row[0].Length);

        Console.WriteLine();
        Console.WriteLine("Summary (sum of stage medians, one vertex + fragment program)");
        Console.WriteLine("=============================================================");

        foreach (var row in rows)
        {
            Console.WriteLine($"  {row[0].PadRight(width)}  {(row.Length > 1 ? row[1] : string.Empty)}");
        }
    }
}
