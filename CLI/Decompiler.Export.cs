using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ValvePak;
using ValveResourceFormat.IO;

namespace CLI;

public partial class Decompiler
{
    /// <summary>
    /// Exports the input files, and the files inside of input packages, on every core.
    /// </summary>
    /// <returns>The number of files written.</returns>
    private int ExportPaths(List<string> paths)
    {
        var packages = new List<Package>();
        var gameFileLoaders = new Dictionary<string, Lazy<GameFileLoader>>(StringComparer.OrdinalIgnoreCase);
        var packageFileLoaders = new List<GameFileLoader>();

        try
        {
            foreach (var path in paths)
            {
#pragma warning disable CA2000 // False positive, packages are disposed in the finally block
                if (IsPackagePath(path) && OpenPackage(path) is { } package)
                {
                    packages.Add(package);
                }
#pragma warning restore CA2000
            }

            var items = new List<ContentExportItem>();

            foreach (var package in packages)
            {
                var fileLoader = CreateGameFileLoader(package, package.FileName);
                packageFileLoaders.Add(fileLoader);

                AddPackageEntries(items, package, fileLoader);
            }

            foreach (var path in paths)
            {
                if (!IsPackagePath(path))
                {
                    AddDiskFile(items, path, gameFileLoaders);
                }
            }

            var exporter = CreateExporter(items);

            try
            {
                exporter.Export(items);
            }
            catch (ArgumentException) when (TargetFile != null)
            {
                ReportError("Output path has a file extension, but more than one file matched. Use --vpk_filepath to match exactly one file, or end the output path with a path separator to write into a folder with that name.");
            }

            return exporter.WrittenFileCount;
        }
        finally
        {
            foreach (var packageFileLoader in packageFileLoaders)
            {
                packageFileLoader.Dispose();
            }

            foreach (var gameFileLoader in gameFileLoaders.Values)
            {
                if (gameFileLoader.IsValueCreated)
                {
                    gameFileLoader.Value.Dispose();
                }
            }

            foreach (var package in packages)
            {
                package.Dispose();
            }
        }
    }

    private static bool IsPackagePath(string path)
    {
        return path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase);
    }

    private Package? OpenPackage(string path)
    {
        PrintFileHeader(path, null);

        var package = new Package();
        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);

        if (TryReadPackage(path, () => package.Read(path)))
        {
            return package;
        }

        package.Dispose();
        return null;
    }

    /// <summary>
    /// Reads a package, and reports why when it could not be read.
    /// </summary>
    private bool TryReadPackage(string path, Action read)
    {
        try
        {
            read();
            return true;
        }
        catch (NotSupportedException e)
        {
            lock (ConsoleWriterLock)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Error.WriteLine($"Failed to open vpk '{path}' - {e.Message}");
                Console.ResetColor();
            }
        }
        catch (Exception e)
        {
            LogException(e, path);
        }

        return false;
    }

    /// <summary>
    /// Gets the loader for the game files of the addon or mod a file is in. Input files in the same one share it, since
    /// mounting a game is slow.
    /// </summary>
    private Lazy<GameFileLoader> GetGameFileLoader(Dictionary<string, Lazy<GameFileLoader>> gameFileLoaders, string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var root = ContentPaths.FindContentRoot(directory) ?? directory;

        if (!gameFileLoaders.TryGetValue(root, out var gameFileLoader))
        {
            gameFileLoader = new Lazy<GameFileLoader>(() => CreateGameFileLoader(null, path));
            gameFileLoaders.Add(root, gameFileLoader);
        }

        return gameFileLoader;
    }

    /// <summary>
    /// Queues the matched files of a package.
    /// </summary>
    private void AddPackageEntries(List<ContentExportItem> items, Package package, IFileLoader fileLoader)
    {
        Debug.Assert(package.Entries != null);

        Console.WriteLine("--- Dumping decompiled files...");

        foreach (var (type, entries) in package.Entries)
        {
            if (!MatchesExtensionFilter(type))
            {
                continue;
            }

            foreach (var (entry, _) in FilteredEntries(entries))
            {
                items.Add(new ContentExportItem(package, entry)
                {
                    FileLoader = fileLoader,
                    Output = Decompile ? DecompileOutput : ContentExportOutput.Raw,
                    TargetFile = TargetFile,
                });
            }
        }
    }

    /// <summary>
    /// Queues a file on disk, which is decompiled whether or not --vpk_decompile is given.
    /// </summary>
    private void AddDiskFile(List<ContentExportItem> items, string path, Dictionary<string, Lazy<GameFileLoader>> gameFileLoaders)
    {
        AnyFileMatched = true;

        items.Add(new ContentExportItem(path, Warn)
        {
            FileLoader = GetGameFileLoader(gameFileLoaders, path).Value,
            Output = DecompileOutput,
            TargetFile = TargetFile,
        });
    }

    private ContentExporter CreateExporter(List<ContentExportItem> items)
    {
        Debug.Assert(OutputFile != null);

        var outputFolder = OutputIsDirectory ? OutputFile : Path.GetDirectoryName(OutputFile)!;
        var outputRoot = OutputToConsole ? outputFolder : ContentPaths.ResolveOutputRoot(outputFolder, items.Select(static item => item.ContentPath));

        if (!OutputToConsole)
        {
            Console.WriteLine($"--- Writing files to \"{outputRoot}\"");
        }

        return new ContentExporter(outputRoot)
        {
            IncludeDependencies = DecompileDependencies,
            ExistingFileAction = SkipExisting ? ExistingFileAction.Skip : ExistingFileAction.Overwrite,
            ExtractOptions = ExtractOptions,
            Progress = ProgressReporter,
            FileWritten = path => PrintLine($"--- Dump written to \"{path}\""),
            FileSkipped = path => PrintLine($"--- Skipped \"{path}\", it already exists"),
            Warning = Warn,
            FileFailed = (fileName, exception) => LogException(exception, fileName),
            GltfExporterFactory = GltfExportFormat != null ? CreateGltfExporter : null,
            ContentFileHandler = OutputToConsole ? PrintContentFile : null,
            MaxDegreeOfParallelism = OutputToConsole ? 1 : Environment.ProcessorCount,
            ManifestOptions = CachedManifest ? GetManifestOptions() : null,
            ItemStarted = item =>
            {
                if (item.Origin != null)
                {
                    PrintFileHeader(item.Origin, null);
                }
            },
            ItemCompleted = item =>
            {
                if (item.Result == ContentExportResult.Unchanged)
                {
                    PrintLine($"--- Skipped (unchanged) \"{item.ContentPath}\"");
                }
                else if (item.Result == ContentExportResult.Handled && !item.IsDependency)
                {
                    PrintLine($"--- Skipped \"{item.ContentPath}\", it is part of a file that was exported");
                }
            },
        };
    }

    /// <summary>
    /// Gets the options that change what --vpk_cache records, other than those the exporter knows of.
    /// </summary>
    private string GetManifestOptions()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"decompile={Decompile} gltf={GltfExportFormat} materials={GltfExportMaterials} animations={GltfExportAnimations} adapt_textures={GltfExportAdaptTextures}" +
            $" extras={GltfExportExtras} compose_additive={GltfComposeAdditive} meshes={string.Join(',', GltfMeshFilter)} animation_list={string.Join(',', GltfAnimationFilter)}");
    }

    /// <summary>
    /// Prints the main file of a decompiled resource, the files it would write alongside are skipped.
    /// </summary>
    private void PrintContentFile(ContentFile contentFile)
    {
        var mainSubFile = contentFile.MainSubFile;
        var contentPath = ContentPaths.Normalize(mainSubFile?.FileName ?? contentFile.FileName);
        var data = mainSubFile != null ? mainSubFile.Extract?.Invoke() : contentFile.Data;
        var skippedFiles = contentFile.AdditionalFiles.Count + contentFile.SubFiles.Count - (mainSubFile != null ? 1 : 0);

        if (data != null)
        {
            PrintFile(contentPath, data);
        }

        if (skippedFiles > 0)
        {
            Console.Error.WriteLine($"--- Not printing {skippedFiles} additional files of \"{contentPath}\", use --output <folder> to write them");
        }
    }

    private void PrintFile(string contentPath, ReadOnlySpan<byte> data)
    {
        lock (ConsoleWriterLock)
        {
            Stdout.WriteLine($"--- {contentPath}");
            Stdout.Flush();

            using var stdout = Console.OpenStandardOutput();
            stdout.Write(data);
            stdout.Write("\n"u8);
        }
    }
}
