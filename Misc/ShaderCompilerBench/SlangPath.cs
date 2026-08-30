using System.Diagnostics;
using System.Globalization;
using System.Text;
using Prowl.Slang;

namespace ShaderCompilerBench;

/// <summary>What Slang is asked to hand back, and what is done with it afterwards.</summary>
internal enum SlangOutput
{
    /// <summary>Slang's own SPIR-V backend, straight into <c>glShaderBinary</c>.</summary>
    Spirv,

    /// <summary>Slang emits GLSL text and the driver's own front end compiles it, as it does today.</summary>
    Glsl,

    /// <summary>Slang emits GLSL text, glslang turns it into SPIR-V 1.0, and the driver specializes that.</summary>
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

    /// <summary>
    /// Loads the native compiler and reports how long that took, once for the whole process, so no
    /// benchmark pays for it. Creating a session is the only way to force the load.
    /// </summary>
    public static string Initialize()
    {
        var description = new SessionDescription
        {
            Targets = [new TargetDescription { Format = CompileTarget.Spirv, Profile = GlobalSession.FindProfile("spirv_1_3") }],
        };

        var start = Stopwatch.GetTimestamp();
        CreateSession(description);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        return string.Create(CultureInfo.InvariantCulture, $"Slang native compiler loaded in {elapsed:F1} ms");
    }

    /// <summary>
    /// Must not be called until every session has been created. In Prowl.Slang 3.2.1 this call
    /// leaves the global session in a state where the next <c>CreateSession</c> faults inside the
    /// native library.
    /// </summary>
    public static string BuildTag() => GlobalSession.GetBuildTagString();

    private sealed record Compiled(byte[] Vertex, byte[] Fragment);

    /// <summary>
    /// Runs one shader through every Slang stage. A fresh session per iteration is what a renderer
    /// keying its variants off preprocessor macros would actually pay, because the macros are fixed
    /// when the session is created.
    /// </summary>
    private static Compiled RunSlang(Timings timings, SlangOutput output, string moduleName, string source, PreprocessorMacroDescription[] macros, ProfileID profile, List<string> warnings)
    {
        var targetDescription = new TargetDescription
        {
            Format = output == SlangOutput.Spirv ? CompileTarget.Spirv : CompileTarget.Glsl,
            Profile = profile,
        };

        var sessionDescription = new SessionDescription
        {
            Targets = [targetDescription],
            SearchPaths = [Sources.ShaderDirectory],
            PreprocessorMacros = macros,
        };

        var session = timings.Measure("slang: create session", () => CreateSession(sessionDescription));

        var module = timings.Measure("slang: parse and check module", () =>
        {
            var loaded = session.LoadModuleFromSourceString(moduleName, moduleName + ".slang", source, out var diagnostics);
            Check(diagnostics, "load module", warnings);
            return loaded;
        });

        var entryPoints = timings.Measure("slang: find entry points", () =>
            new[] { module.FindEntryPointByName(VertexEntryPoint), module.FindEntryPointByName(FragmentEntryPoint) });

        var composite = timings.Measure("slang: compose program", () =>
        {
            var result = session.CreateCompositeComponentType([module, entryPoints[0], entryPoints[1]], out var diagnostics);
            Check(diagnostics, "compose", warnings);
            return result;
        });

        var linked = timings.Measure("slang: link", () =>
        {
            var result = composite.Link(out var diagnostics);
            Check(diagnostics, "link", warnings);
            return result;
        });

        var vertexCode = timings.Measure("slang: emit vertex code", () => EntryPointCode(linked, 0, warnings));
        var fragmentCode = timings.Measure("slang: emit fragment code", () => EntryPointCode(linked, 1, warnings));

        // Every one of these owns a COM reference the finalizer thread would release, and a release
        // landing in the middle of the next compile is one of the ways this wrapper corrupts memory.
        GC.KeepAlive(session);
        GC.KeepAlive(module);
        GC.KeepAlive(entryPoints);
        GC.KeepAlive(composite);
        GC.KeepAlive(linked);

        return new Compiled(vertexCode, fragmentCode);
    }

    /// <summary>
    /// Takes the description by value so the pointer the native side is handed points at a stack
    /// local. Passing a lambda's captured copy hands it an interior pointer into a heap object,
    /// which the compiler reads straight through and faults on.
    /// </summary>
    private static Session CreateSession(SessionDescription description)
        => GlobalSession.CreateSession(in description);

    private static byte[] EntryPointCode(ComponentType program, int entryPoint, List<string> warnings)
    {
        var code = program.GetEntryPointCode(entryPoint, 0, out var diagnostics);
        Check(diagnostics, "emit code", warnings);
        return code.ToArray();
    }

    /// <summary>
    /// Warnings are collected rather than thrown, because Slang reports things like "SPIR-V version
    /// too old" as a warning and then emits a newer version anyway, which is exactly the kind of
    /// answer this bench exists to surface.
    /// </summary>
    private static void Check(DiagnosticInfo diagnostics, string stage, List<string> warnings)
    {
        var failed = false;

        foreach (var diagnostic in diagnostics.GetDiagnostics())
        {
            if (diagnostic.Severity >= Severity.Error)
            {
                failed = true;
            }
            else if (diagnostic.Severity >= Severity.Warning)
            {
                var text = $"{stage}: {diagnostic.Message.Trim()}";

                if (!warnings.Contains(text, StringComparer.Ordinal))
                {
                    warnings.Add(text);
                }
            }
        }

        if (failed)
        {
            throw new InvalidOperationException($"Slang failed to {stage}: {diagnostics.Message}");
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
        }

        var profile = GlobalSession.FindProfile(profileName);
        timings.Notes.Add($"Slang profile '{profileName}' resolved to {(int)profile}"
            + ((int)profile == 0 ? " (unknown, Slang falls back to its default)" : string.Empty));

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
                .Select(macro => new PreprocessorMacroDescription { Name = macro.Name, Value = macro.Value })
                .ToArray();

            // Run the finalizers for the previous iteration's Slang objects now, so they cannot run
            // on top of this iteration's compile, and so their cost is not charged to a stage.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            GlobalSession.GetCompilerElapsedTime(out var totalBefore, out var downstreamBefore);

            var compiled = RunSlang(target, output, moduleName, source, iterationMacros, profile, warnings);

            GlobalSession.GetCompilerElapsedTime(out var totalAfter, out var downstreamAfter);

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
        timings.Notes.AddRange(warnings.Select(warning => "warning: " + warning));
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
