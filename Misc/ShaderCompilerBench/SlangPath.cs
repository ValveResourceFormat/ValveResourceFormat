using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SlangShaderSharp;

namespace ShaderCompilerBench;

/// <summary>What Slang is asked to hand back, and what is done with it afterwards.</summary>
internal enum SlangOutput
{
    /// <summary>Slang's own SPIR-V backend, straight into <c>glShaderBinary</c>.</summary>
    Spirv,

    /// <summary>Slang emits GLSL text and the driver's own front end compiles it, as it does today.</summary>
    Glsl,

    /// <summary>Slang emits GLSL text, glslang turns it into SPIR-V, and the driver specializes that.</summary>
    GlslThenGlslang,
}

internal static class SlangPath
{
    private const string VertexEntryPoint = "vertexMain";
    private const string FragmentEntryPoint = "fragmentMain";

    /// <summary>
    /// What the entry point is called inside the SPIR-V. Slang renames every entry point to
    /// <c>main</c>, which is also what glslang produces and what OpenGL expects.
    /// </summary>
    public const string SpirvEntryPoint = "main";

    private static IGlobalSession? globalSession;

    private static IGlobalSession Global => globalSession ??= CreateGlobalSession();

    /// <summary>
    /// Whether <c>slang-glslang</c>, which carries spirv-opt and the SPIR-V validator, could be
    /// found. Slang loads it by bare name from the process directory, so the copy the package puts
    /// under <c>runtimes/</c> is invisible to it unless something has loaded it into the process
    /// first, after which the name resolves to the module already there.
    /// </summary>
    private static bool downstreamLoaded;

    private static IGlobalSession CreateGlobalSession()
    {
        downstreamLoaded = NativeLibrary.TryLoad("slang-glslang", Assembly.GetExecutingAssembly(), null, out _);

        var result = Slang.CreateGlobalSession(Slang.ApiVersion, out var session);

        if (result.Failed || session == null)
        {
            throw new InvalidOperationException($"Slang would not create a global session: {result.GetSymbolicName()}");
        }

        return session;
    }

    /// <summary>
    /// Loads the native compiler and reports how long that took, once for the whole process, so no
    /// benchmark pays for it.
    /// </summary>
    public static string Initialize()
    {
        var start = Stopwatch.GetTimestamp();
        var version = Global.GetBuildTagString();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        return string.Create(CultureInfo.InvariantCulture, $"Slang {version} loaded in {elapsed:F1} ms");
    }

    public static string BuildTag() => Global.GetBuildTagString();

    private sealed record Compiled(byte[] Vertex, byte[] Fragment);

    /// <summary>
    /// Runs one shader through every Slang stage. A fresh session per iteration is what a renderer
    /// keying its variants off preprocessor macros would actually pay, because the macros are fixed
    /// when the session is created.
    /// </summary>
    private static Compiled RunSlang(Timings timings, SlangOutput output, string moduleName, string source, PreprocessorMacroDesc[] macros, SlangProfileID profile, List<string> warnings)
    {
        var sessionDescription = new SessionDesc
        {
            Targets =
            [
                new TargetDesc
                {
                    Format = output == SlangOutput.Spirv ? SlangCompileTarget.Spirv : SlangCompileTarget.Glsl,
                    Profile = profile,
                },
            ],
            SearchPaths = [Sources.ShaderDirectory],
            PreprocessorMacros = macros,
        };

        var session = timings.Measure("slang: create session", () =>
        {
            Check(Global.CreateSession(in sessionDescription, out var created), null, "create session", warnings);
            return created ?? throw new InvalidOperationException("Slang returned no session");
        });

        var module = timings.Measure("slang: parse and check module", () =>
        {
            var loaded = session.LoadModuleFromSourceString(moduleName, moduleName + ".slang", source, out var diagnostics);
            Check(loaded == null ? SlangResult.SLANG_FAIL : SlangResult.SLANG_OK, diagnostics, "load module", warnings);
            return loaded!;
        });

        var entryPoints = timings.Measure("slang: find entry points", () =>
        {
            Check(module.FindEntryPointByName(VertexEntryPoint, out var vertex), null, "find " + VertexEntryPoint, warnings);
            Check(module.FindEntryPointByName(FragmentEntryPoint, out var fragment), null, "find " + FragmentEntryPoint, warnings);
            return new IComponentType[] { module, vertex!, fragment! };
        });

        var composite = timings.Measure("slang: compose program", () =>
        {
            Check(session.CreateCompositeComponentType(entryPoints, out var created, out var diagnostics), diagnostics, "compose", warnings);
            return created!;
        });

        var linked = timings.Measure("slang: link", () =>
        {
            Check(composite.Link(out var result, out var diagnostics), diagnostics, "link", warnings);
            return result!;
        });

        var vertexCode = timings.Measure("slang: emit vertex code", () => EntryPointCode(linked, 0, warnings));
        var fragmentCode = timings.Measure("slang: emit fragment code", () => EntryPointCode(linked, 1, warnings));

        return new Compiled(vertexCode, fragmentCode);
    }

    private static byte[] EntryPointCode(IComponentType program, int entryPoint, List<string> warnings)
    {
        var result = program.GetEntryPointCode(entryPoint, 0, out var code, out var diagnostics);
        Check(result, diagnostics, "emit code", warnings);
        return code!.Buffer.ToArray();
    }

    /// <summary>
    /// Slang reports things like "SPIR-V version too old" as a warning and then emits a newer
    /// version anyway, which is exactly the kind of answer this bench exists to surface, so the
    /// result code decides whether a stage failed and the blob is only text to carry along.
    /// </summary>
    private static void Check(SlangResult result, ISlangBlob? diagnostics, string stage, List<string> warnings)
    {
        var message = diagnostics?.AsString?.Trim() ?? string.Empty;

        if (result.Failed)
        {
            throw new InvalidOperationException(
                $"Slang failed to {stage}{(message.Length == 0 ? $" ({result.GetSymbolicName()})" : ":\n" + message)}");
        }

        if (message.Length == 0)
        {
            return;
        }

        var text = $"{stage}: {message}";

        if (!warnings.Contains(text, StringComparer.Ordinal))
        {
            warnings.Add(text);
        }
    }

    public static Timings Run(string title, SlangOutput output, string moduleName, BenchmarkOptions options, string profileName)
    {
        var timings = new Timings(title);
        var source = Sources.Read(moduleName + ".slang");

        if (output == SlangOutput.GlslThenGlslang)
        {
            if (Glslang.Initialize() is string unavailable)
            {
                timings.Skipped = unavailable;
                return timings;
            }

            timings.Notes.Add($"glslang targeting SPIR-V {Glslang.Resolve(options.SpirvVersion)}");
            timings.Notes.Add(Glslang.DescribeBindings());
            timings.Notes.Add(SpirvOptimizer.Describe());
        }

        var profile = Global.FindProfile(profileName);
        timings.Notes.Add($"Slang profile '{profileName}' resolved to {(int)profile}"
            + ((int)profile == 0 ? " (unknown, Slang falls back to its default)" : string.Empty));

        if (output == SlangOutput.Spirv)
        {
            timings.Notes.Add(downstreamLoaded
                ? "slang-glslang loaded, so Slang runs spirv-opt over its SPIR-V"
                : "slang-glslang not found, so Slang emits unoptimized SPIR-V");
        }

        var warnings = new List<string>();
        Compiled? last = null;
        SpirvPair? lastSpirv = null;

        for (var i = 0; i < options.Warmup + options.Iterations; i++)
        {
            var isWarmup = i < options.Warmup;
            var salt = options.Salt(i);
            var target = isWarmup ? new Timings(title) : timings;

            IEnumerable<(string Name, string Value)> withSalt =
                [.. options.Macros, ("BENCH_SALT", salt.ToString(CultureInfo.InvariantCulture))];

            var iterationMacros = withSalt
                .Select(macro => new PreprocessorMacroDesc(macro.Name, macro.Value))
                .ToArray();

            // Run the finalizers for the previous iteration's Slang objects now, so they cannot run
            // on top of this iteration's compile, and so their cost is not charged to a stage.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Global.GetCompilerElapsedTime(out var totalBefore, out var downstreamBefore);

            var compiled = RunSlang(target, output, moduleName, source, iterationMacros, profile, warnings);

            Global.GetCompilerElapsedTime(out var totalAfter, out var downstreamAfter);

            // Slang's own accounting, kept next to ours so the two can be checked against each other.
            target.Add(Timings.InformationalPrefix + " slang self-reported total", (totalAfter - totalBefore) * 1000.0);
            target.Add(Timings.InformationalPrefix + " slang self-reported downstream", (downstreamAfter - downstreamBefore) * 1000.0);

            if (output == SlangOutput.Spirv)
            {
                lastSpirv = new SpirvPair(compiled.Vertex, compiled.Fragment);
                SpirvDriver.Run(target, lastSpirv, SpirvEntryPoint);
            }
            else
            {
                var glsl = new SourcePair(moduleName,
                    Encoding.UTF8.GetString(compiled.Vertex),
                    Encoding.UTF8.GetString(compiled.Fragment));

                if (output == SlangOutput.Glsl)
                {
                    GlslPath.Run(target, glsl, salt: null);
                }
                else
                {
                    lastSpirv = Glslang.Compile(target, glsl, GlslOrigin.ForSpirv);
                    SpirvDriver.Run(target, lastSpirv, SpirvEntryPoint);
                }
            }

            last = compiled;
        }

        Describe(timings, output, moduleName, source, last!, lastSpirv, options.Dump);
        timings.Notes.AddRange(warnings.Select(warning => "warning: " + warning.ReplaceLineEndings(" ")));
        return timings;
    }

    private static void Describe(Timings timings, SlangOutput output, string moduleName, string source, Compiled compiled, SpirvPair? spirv, bool dump)
    {
        timings.Notes.Insert(0, string.Create(CultureInfo.InvariantCulture,
            $"source: {moduleName}.slang, {source.AsSpan().Count('\n') + 1} lines, {source.Length / 1024.0:F1} KiB"));

        if (output == SlangOutput.Spirv)
        {
            timings.Notes.Add("Slang " + Glslang.Describe(new SpirvPair(compiled.Vertex, compiled.Fragment)));
        }
        else
        {
            timings.Notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"Slang emitted: vertex {compiled.Vertex.Length} GLSL bytes, fragment {compiled.Fragment.Length} GLSL bytes"));

            if (spirv != null)
            {
                timings.Notes.Add("glslang " + Glslang.Describe(spirv));
            }
        }

        if (!dump)
        {
            return;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "dump");
        Directory.CreateDirectory(directory);
        var suffix = output.ToString().ToLowerInvariant();
        var extension = output == SlangOutput.Spirv ? "spv" : "glsl";

        File.WriteAllBytes(Path.Combine(directory, $"{moduleName}.{suffix}.vert.{extension}"), compiled.Vertex);
        File.WriteAllBytes(Path.Combine(directory, $"{moduleName}.{suffix}.frag.{extension}"), compiled.Fragment);

        if (spirv != null && output != SlangOutput.Spirv)
        {
            File.WriteAllBytes(Path.Combine(directory, $"{moduleName}.{suffix}.vert.spv"), spirv.Vertex);
            File.WriteAllBytes(Path.Combine(directory, $"{moduleName}.{suffix}.frag.spv"), spirv.Fragment);
        }

        timings.Notes.Add("dumped to " + directory);
    }
}
