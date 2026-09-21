using System.IO;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace VmdlExtractor;

/// <summary>
/// Options controlling a single <see cref="CustomModelExporter.ExtractModels"/> run.
/// </summary>
public sealed class CustomModelExportOptions
{
    /// <summary>Mirrors the CLI's -v/--verbose flag; emits detailed per-file diagnostics.</summary>
    public bool Verbose { get; set; }

    /// <summary>When true, runs resourcecompiler.exe on the exported .vmdl files afterwards and
    /// applies the motion-array patch and MRPH/PHYS fidelity transplant from <see cref="DonorVpkPath"/>.</summary>
    public bool AutoBuild { get; set; }

    /// <summary>Path to resourcecompiler.exe. Required when <see cref="AutoBuild"/> is true.</summary>
    public string? ResourceCompilerPath { get; set; }

    /// <summary>The "-game" directory passed to resourcecompiler.exe. Required when <see cref="AutoBuild"/> is true.</summary>
    public string? GameDir { get; set; }

    /// <summary>
    /// Path to the original .vpk the models were extracted from, used to patch m_movementArray and
    /// transplant MRPH/PHYS blocks into the recompiled output. Auto-build still runs without it, just
    /// without those two fidelity steps.
    /// </summary>
    public string? DonorVpkPath { get; set; }

    /// <summary>
    /// When true, every target's files are written directly into <c>outputRoot</c> instead of under
    /// a subfolder mirroring its path inside the source VPK. Use this when the caller picked an exact
    /// destination folder for a single file (so its output replaces what's already there), not when
    /// exporting a batch of files that need to stay untangled from each other.
    /// </summary>
    public bool FlattenOutput { get; set; }

    /// <summary>
    /// Renames just the top-level .vmdl of the first (and only meaningful when there is exactly one)
    /// initial target, e.g. to drop an arcana's content in as a hero's base model file. Recursively
    /// discovered include-model dependencies always keep their own name, since the model that
    /// references them still looks them up by their original path.
    /// </summary>
    public string? OutputBasename { get; set; }
}

/// <summary>A single file that failed to export, with the reason.</summary>
public sealed class CustomModelExportFailure
{
    /// <summary>Relative path of the file that failed.</summary>
    public required string File { get; init; }

    /// <summary>Exception message describing the failure.</summary>
    public required string Error { get; init; }
}

/// <summary>Outcome of a <see cref="CustomModelExporter.ExtractModels"/> run.</summary>
public sealed class CustomModelExportResult
{
    /// <summary>Number of models (including recursively-queued include-deps) exported successfully.</summary>
    public int Succeeded { get; internal set; }

    /// <summary>Models that failed to export, with their errors.</summary>
    public List<CustomModelExportFailure> Failures { get; } = [];

    /// <summary>Whether auto-build was requested for this run.</summary>
    public bool AutoBuildAttempted { get; internal set; }

    /// <summary>Whether auto-build was requested but did not complete successfully.</summary>
    public bool AutoBuildFailed { get; internal set; }

    /// <summary>Human-readable auto-build failure reason, set when <see cref="AutoBuildFailed"/> is true.</summary>
    public string? AutoBuildError { get; internal set; }
}

/// <summary>
/// GUI-facing entry point into the ModelDoc-fidelity decompile pipeline implemented in <see cref="Program"/>.
/// Unlike the standalone CLI, this resolves every dependency (including recursive
/// m_refAnimIncludeModels) through the caller's own <see cref="IFileLoader"/>, so it works the same
/// way whether the source file lives in a mounted VPK, an add-on folder, or on disk.
/// </summary>
public static class CustomModelExporter
{
    /// <summary>
    /// Runs the custom decompile pipeline over <paramref name="initialTargets"/>, following
    /// m_refAnimIncludeModels dependencies, then optionally auto-builds the result.
    /// </summary>
    /// <param name="initialTargets">
    /// The models to export, as (compiled relative path, already-read Resource) pairs. The caller
    /// keeps ownership of these resources (and should dispose them once this call returns); any
    /// include-model dependency discovered along the way is instead resolved and owned by
    /// <paramref name="fileLoader"/>.
    /// </param>
    /// <param name="outputRoot">Destination folder. Created if missing.</param>
    /// <param name="fileLoader">Resolves dependencies (meshes, physics, textures, include models).</param>
    /// <param name="options">Export options.</param>
    public static CustomModelExportResult ExtractModels(
        IReadOnlyList<(string RelativePath, Resource Resource)> initialTargets,
        string outputRoot,
        IFileLoader fileLoader,
        CustomModelExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(initialTargets);
        ArgumentNullException.ThrowIfNull(outputRoot);
        ArgumentNullException.ThrowIfNull(fileLoader);
        ArgumentNullException.ThrowIfNull(options);

        var result = new CustomModelExportResult();
        Directory.CreateDirectory(outputRoot);

        byte[]? RawReader(string path)
        {
            using var stream = fileLoader.GetFileStream(path);
            if (stream == null)
            {
                return null;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var includeQueue = new Queue<string>();

        void ProcessOne(string relativePath, Resource resource, string? outputBasename)
        {
            try
            {
                var modelFolder = options.FlattenOutput
                    ? outputRoot
                    : Program.ComputeModelFolder(outputRoot, relativePath, noModelFolder: true);
                Directory.CreateDirectory(modelFolder);

                var includes = Program.ProcessOneModel(
                    resource, modelFolder, fileLoader, RawReader,
                    noRawDeps: false, noSanitize: false, options.Verbose, outputBasename);

                result.Succeeded++;

                foreach (var include in includes)
                {
                    if (visited.Add(include))
                    {
                        includeQueue.Enqueue(include);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Failures.Add(new CustomModelExportFailure { File = relativePath, Error = ex.ToString() });
            }
        }

        foreach (var (relativePath, resource) in initialTargets)
        {
            if (visited.Add(StripCompiledSuffix(relativePath)))
            {
                ProcessOne(relativePath, resource, options.OutputBasename);
            }
        }

        while (includeQueue.TryDequeue(out var include))
        {
            var compiledPath = include + GameFileLoader.CompiledFileSuffix;
            var resource = fileLoader.LoadFileCompiled(include);

            if (resource == null)
            {
                result.Failures.Add(new CustomModelExportFailure
                {
                    File = compiledPath,
                    Error = "Could not resolve include-model dependency",
                });
                continue;
            }

            ProcessOne(compiledPath, resource, outputBasename: null);
        }

        if (options.AutoBuild)
        {
            result.AutoBuildAttempted = true;

            if (string.IsNullOrWhiteSpace(options.ResourceCompilerPath) || string.IsNullOrWhiteSpace(options.GameDir))
            {
                result.AutoBuildFailed = true;
                result.AutoBuildError = "Resource compiler path and game directory are both required for auto-build.";
            }
            else
            {
                var exitCode = Program.AutoBuildAndPatch(
                    outputRoot, options.ResourceCompilerPath, options.DonorVpkPath, options.Verbose, options.GameDir);

                if (exitCode != 0)
                {
                    result.AutoBuildFailed = true;
                    result.AutoBuildError = $"Auto-build exited with code {exitCode}, see log for details.";
                }
            }
        }

        return result;
    }

    private static string StripCompiledSuffix(string path) =>
        path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
            ? path[..^GameFileLoader.CompiledFileSuffix.Length]
            : path;
}
