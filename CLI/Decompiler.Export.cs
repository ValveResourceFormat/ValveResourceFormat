using System.Diagnostics;
using System.Globalization;
using System.IO;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ToolsAssetInfo;

namespace CLI;

public partial class Decompiler
{
    /// <summary>
    /// The files of a package that were exported, with their checksums, so that --vpk_cache skips them while they stay
    /// the same. Stored in the output root, and only used again by an export with the same version and options.
    /// </summary>
    private sealed class VpkCache(string filePath, string header)
    {
        public string FilePath { get; } = filePath;
        public string Header { get; } = header;
        public Dictionary<string, uint> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(ContentExportItem Item, uint Crc32)> Queued { get; } = [];
    }

    /// <summary>
    /// Exports the input files, and the files inside of input packages, on every core.
    /// </summary>
    private void ExportPaths(List<string> paths)
    {
        var packages = new List<(string Path, Package Package)>();
        var gameFileLoaders = new Dictionary<string, Lazy<GameFileLoader>>(StringComparer.OrdinalIgnoreCase);
        var caches = new List<(VpkCache Cache, Package Package)>();
        var packageFileLoaders = new List<GameFileLoader>();

        try
        {
            foreach (var path in paths)
            {
#pragma warning disable CA2000 // False positive, packages are disposed in the finally block
                if (IsPackagePath(path) && OpenPackage(path) is { } package)
                {
                    packages.Add((path, package));
                }
#pragma warning restore CA2000
            }

            var isSinglePackage = LinkedFilePath == null && paths.Count == 1 && packages.Count == 1;
            Exporter = CreateExporter(isSinglePackage ? GetMatchedEntriesFolder(packages[0].Package) : GetContentFolder(paths));

            var items = new List<ContentExportItem>();

            foreach (var (path, package) in packages)
            {
                var fileLoader = CreateGameFileLoader(package, package.FileName);
                packageFileLoaders.Add(fileLoader);

                if (AddPackageEntries(items, path, package, fileLoader) is { } cache)
                {
                    caches.Add((cache, package));
                }
            }

            foreach (var path in paths)
            {
                if (!IsPackagePath(path))
                {
                    AddDiskFile(items, path, gameFileLoaders);
                }
            }

            Exporter.Export(items);

            foreach (var (cache, package) in caches)
            {
                SaveVpkCache(cache, package);
            }
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

            foreach (var (_, package) in packages)
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

        try
        {
            package.Read(path);
            return package;
        }
        catch (NotSupportedException e)
        {
            package.Dispose();

            lock (ConsoleWriterLock)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Error.WriteLine($"Failed to open vpk '{path}' - {e.Message}");
                Console.ResetColor();
            }
        }
        catch (Exception e)
        {
            package.Dispose();
            LogException(e, path);
        }

        return null;
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
            gameFileLoader = new Lazy<GameFileLoader>(() => CreateSharedGameFileLoader(path));
            gameFileLoaders.Add(root, gameFileLoader);
        }

        return gameFileLoader;
    }

    /// <summary>
    /// Queues the matched files of a package.
    /// </summary>
    /// <returns>The cache to save once they were exported, with --vpk_cache.</returns>
    private VpkCache? AddPackageEntries(List<ContentExportItem> items, string path, Package package, IFileLoader fileLoader)
    {
        Debug.Assert(package.Entries != null);

        if (!OutputIsDirectory && MatchesMultipleFiles(package))
        {
            ReportError("Output path has a file extension, but more than one file matched. Use --vpk_filepath to match exactly one file, or end the output path with a path separator to write into a folder with that name.");
            return null;
        }

        Console.WriteLine("--- Dumping decompiled files...");

        var cache = CachedManifest ? LoadVpkCache(path) : null;
        var source = ContentExportSource.FromPackage(package);

        foreach (var (type, entries) in package.Entries)
        {
            if (!MatchesExtensionFilter(type))
            {
                continue;
            }

            foreach (var (entry, _) in FilteredEntries(entries))
            {
                var contentPath = entry.GetFullPath();

                if (cache != null && cache.Entries.TryGetValue(contentPath, out var crc32) && crc32 == entry.CRC32)
                {
                    Console.WriteLine($"--- Skipped (unchanged) \"{contentPath}\"");
                    continue;
                }

                var item = new ContentExportItem
                {
                    ContentPath = contentPath,
                    Open = () => GameFileLoader.GetPackageEntryStream(package, entry),
                    Crc32 = entry.CRC32 > 0 ? entry.CRC32 : null,
                    Source = source,
                    FileLoader = fileLoader,
                    Output = Decompile ? DecompileOutput : ContentExportOutput.Raw,
                    TargetFile = TargetFile,
                };

                items.Add(item);
                cache?.Queued.Add((item, entry.CRC32));
            }
        }

        return cache;
    }

    /// <summary>
    /// Queues a file on disk, which is decompiled whether or not --vpk_decompile is given.
    /// </summary>
    private void AddDiskFile(List<ContentExportItem> items, string path, Dictionary<string, Lazy<GameFileLoader>> gameFileLoaders)
    {
        AnyFileMatched = true;

        var contentPath = GetContentPath(path, null);
        var gameFileLoader = GetGameFileLoader(gameFileLoaders, path);

        if (!ContentPaths.HasCompiledSuffix(contentPath))
        {
            items.Add(new ContentExportItem
            {
                ContentPath = contentPath,
                Open = () => File.OpenRead(path),
                Origin = path,
                Output = DecompileOutput,
                TargetFile = TargetFile,
                ExportFile = (stream, _) => ExportDiskFile(path, contentPath, stream, gameFileLoader),
            });

            return;
        }

        var source = ContentExportSource.FromDiskFile(path);

        if (source == null && DecompileDependencies)
        {
            Warn($"Dependencies of \"{path}\" are not decompiled, it is not in an addon or mod folder.");
        }

        items.Add(new ContentExportItem
        {
            ContentPath = contentPath,
            Open = () => File.OpenRead(path),
            Origin = path,
            Source = source,
            FileLoader = gameFileLoader.Value,
            Output = DecompileOutput,
            TargetFile = TargetFile,
        });
    }

    /// <summary>
    /// Exports a file on disk that is neither a compiled resource nor a file the library extracts, including the formats
    /// only the CLI reads.
    /// </summary>
    private bool ExportDiskFile(string path, string contentPath, Stream stream, Lazy<GameFileLoader> gameFileLoader)
    {
        var magic = PeekMagic(stream);

        switch (magic)
        {
            case VfxProgramData.MAGIC:
                ParseVCS(path, stream, null);
                return true;

            case ToolsAssetInfo.MAGIC2:
            case ToolsAssetInfo.MAGIC:
                ParseToolsAssetInfo(path, stream, null);
                return true;
        }

        if (BinaryKV3.IsBinaryKV3(magic))
        {
            return false;
        }

        using var resource = new Resource
        {
            FileName = contentPath,
        };

        resource.Read(stream);
        Exporter!.Export(resource, gameFileLoader.Value, ContentExportSource.FromDiskFile(path), TargetFile);

        return true;
    }

    private VpkCache LoadVpkCache(string packagePath)
    {
        Debug.Assert(Exporter != null);

        var cache = new VpkCache(
            Path.Join(Exporter.OutputRoot, string.Concat(Path.GetFileName(packagePath), ".manifest.txt")),
            GetVpkCacheHeader(packagePath));

        if (!File.Exists(cache.FilePath))
        {
            return cache;
        }

        using var reader = new StreamReader(cache.FilePath);

        if (reader.ReadLine() != cache.Header)
        {
            Console.Error.WriteLine($"\"{cache.FilePath}\" was written by another version, for another package or with other options, it is ignored.");
            return cache;
        }

        while (reader.ReadLine() is { } line)
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);

            if (space > 0 && uint.TryParse(line.AsSpan(0, space), NumberStyles.None, CultureInfo.InvariantCulture, out var crc32))
            {
                cache.Entries[line[(space + 1)..]] = crc32;
            }
        }

        return cache;
    }

    private string GetVpkCacheHeader(string packagePath)
    {
        var version = typeof(Decompiler).Assembly.GetName().Version;

        return string.Create(CultureInfo.InvariantCulture,
            $"// s2v_version={version} package={Path.GetFullPath(packagePath)} decompile={Decompile} dependencies={DecompileDependencies} {ExtractOptions}" +
            $" gltf={GltfExportFormat} materials={GltfExportMaterials} animations={GltfExportAnimations} adapt_textures={GltfExportAdaptTextures}" +
            $" extras={GltfExportExtras} compose_additive={GltfComposeAdditive} meshes={string.Join(',', GltfMeshFilter)} animation_list={string.Join(',', GltfAnimationFilter)}");
    }

    /// <summary>
    /// Records the files that exported, drops the ones that failed so the next export retries them.
    /// </summary>
    private static void SaveVpkCache(VpkCache cache, Package package)
    {
        foreach (var (item, crc32) in cache.Queued)
        {
            if (item.Result is ContentExportResult.Exported or ContentExportResult.Handled)
            {
                cache.Entries[item.ContentPath] = crc32;
            }
            else
            {
                cache.Entries.Remove(item.ContentPath);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cache.FilePath)!);

        using var writer = new StreamWriter(cache.FilePath);
        writer.WriteLine(cache.Header);

        foreach (var (contentPath, crc32) in cache.Entries)
        {
            if (package.FindEntry(contentPath) == null)
            {
                Console.WriteLine($"\t{contentPath} no longer exists in VPK");
                continue;
            }

            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{crc32} {contentPath}"));
        }
    }
}
