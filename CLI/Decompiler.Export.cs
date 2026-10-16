using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.NavMesh;
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

            var queue = new ContentExportQueue(Exporter)
            {
                MaxDegreeOfParallelism = OutputToConsole ? 1 : Environment.ProcessorCount,
                ItemStarted = item =>
                {
                    if (item.Origin != null)
                    {
                        PrintFileHeader(item.Origin, null);
                    }
                },
                ItemCompleted = item =>
                {
                    if (item.Result == ContentExportResult.Handled && !item.IsDependency)
                    {
                        PrintLine($"--- Skipped \"{item.ContentPath}\", it is part of a file that was exported");
                    }
                },
            };

            foreach (var (path, package) in packages)
            {
                var fileLoader = CreateGameFileLoader(package, package.FileName);
                packageFileLoaders.Add(fileLoader);

                if (AddPackageEntries(queue, path, package, fileLoader) is { } cache)
                {
                    caches.Add((cache, package));
                }
            }

            foreach (var path in paths)
            {
                if (!IsPackagePath(path))
                {
                    AddDiskFile(queue, path, gameFileLoaders);
                }
            }

            queue.Run();

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
    private VpkCache? AddPackageEntries(ContentExportQueue queue, string path, Package package, IFileLoader fileLoader)
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
        var exportResource = CreateResourceExport(fileLoader, warnTargetExtension: false);
        var shaderFilesToDecompile = Decompile && package.Entries.TryGetValue("vcs", out var shaderEntries) ? GetShaderFilesToDecompile(shaderEntries) : null;

        foreach (var (type, entries) in package.Entries)
        {
            if (!MatchesExtensionFilter(type))
            {
                continue;
            }

            foreach (var (entry, filePath) in FilteredEntries(entries))
            {
                var contentPath = entry.GetFullPath();

                if (cache != null && cache.Entries.TryGetValue(contentPath, out var crc32) && crc32 == entry.CRC32)
                {
                    Console.WriteLine($"--- Skipped (unchanged) \"{contentPath}\"");
                    continue;
                }

                Func<Stream, CancellationToken, bool>? exportFile = null;

                // VCS files require multiple files to be decompiled together, once from the highest shader model that was matched
                if (shaderFilesToDecompile != null && type == "vcs")
                {
                    if (!shaderFilesToDecompile.Contains(filePath))
                    {
                        continue;
                    }

                    exportFile = (_, _) => ExportShader(package, filePath);
                }
                else if (OutputToConsole)
                {
                    exportFile = (stream, _) => PrintNonResource(contentPath, stream);
                }

                var item = new ContentExportItem
                {
                    ContentPath = contentPath,
                    Open = () => GameFileLoader.GetPackageEntryStream(package, entry),
                    Crc32 = entry.CRC32 > 0 ? entry.CRC32 : null,
                    Source = source,
                    FileLoader = fileLoader,
                    Decompile = Decompile,
                    TargetFile = TargetFile,
                    ExportResource = exportResource,
                    ExportFile = exportFile,
                };

                queue.Add(item);
                cache?.Queued.Add((item, entry.CRC32));
            }
        }

        return cache;
    }

    /// <summary>
    /// Queues a file on disk, which is decompiled whether or not --vpk_decompile is given.
    /// </summary>
    private void AddDiskFile(ContentExportQueue queue, string path, Dictionary<string, Lazy<GameFileLoader>> gameFileLoaders)
    {
        AnyFileMatched = true;

        var contentPath = GetContentPath(path, null);
        var gameFileLoader = GetGameFileLoader(gameFileLoaders, path);

        if (!ContentPaths.HasCompiledSuffix(contentPath))
        {
            queue.Add(new ContentExportItem
            {
                ContentPath = contentPath,
                Open = () => File.OpenRead(path),
                Origin = path,
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

        queue.Add(new ContentExportItem
        {
            ContentPath = contentPath,
            Open = () => File.OpenRead(path),
            Origin = path,
            Source = source,
            FileLoader = gameFileLoader.Value,
            TargetFile = TargetFile,
            ExportResource = CreateResourceExport(gameFileLoader.Value, warnTargetExtension: true),
        });
    }

    /// <summary>
    /// Exports a file on disk that is not a compiled resource, including the formats only the CLI reads.
    /// </summary>
    private bool ExportDiskFile(string path, string contentPath, Stream stream, Lazy<GameFileLoader> gameFileLoader)
    {
        var magic = PeekMagic(stream);

        switch (magic)
        {
            case VfxProgramData.MAGIC:
                ParseVCS(path, stream, null);
                return true;

            case NavMeshFile.MAGIC when GltfExportFormat != null:
                ExportNavMeshGltf(path, contentPath, stream);
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

        if (TryParseNonResource(path, contentPath, stream, null))
        {
            return true;
        }

        using var resource = new Resource
        {
            FileName = contentPath,
        };

        resource.Read(stream);
        ExportResource(resource, gameFileLoader.Value, ContentExportSource.FromDiskFile(path));

        return true;
    }

    /// <summary>
    /// Gets how a compiled resource is exported instead of being decompiled: printed, or to glTF.
    /// </summary>
    private Func<Resource, CancellationToken, bool>? CreateResourceExport(IFileLoader fileLoader, bool warnTargetExtension)
    {
        if (OutputToConsole)
        {
            return (resource, _) =>
            {
                ExportResource(resource, fileLoader, null);
                return true;
            };
        }

        var targetExtension = warnTargetExtension ? Path.GetExtension(TargetFile) : null;
        var gltfExporter = GltfExportFormat != null ? CreateGltfExporter(fileLoader) : null;

        if (gltfExporter == null && targetExtension == null)
        {
            return null;
        }

        return (resource, _) =>
        {
            if (gltfExporter != null && GltfModelExporter.CanExport(resource))
            {
                ExportGltf(gltfExporter, resource);
                return true;
            }

            var extension = FileExtract.GetExtension(resource) ?? ContentPaths.TrimCompiledSuffix(Path.GetExtension(resource.FileName!));

            if (targetExtension != null && !targetExtension.Equals($".{extension}", StringComparison.OrdinalIgnoreCase))
            {
                Warn($"Extension '.{extension}' might be more suitable than the one provided '{targetExtension}'");
            }

            return false;
        };
    }

    /// <summary>
    /// Decompiles a shader to a <c>.vfx</c> file from all of its files in the package.
    /// </summary>
    private bool ExportShader(Package package, string filePath)
    {
        // Remove the last part from the file name ("_features", ...)
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var newFileNameBase = fileName.AsSpan(0, fileName.LastIndexOf('_'));
        var vfxFileName = ContentPaths.Join(Path.GetDirectoryName(filePath), string.Concat(newFileNameBase, ".vfx"));

        using var collection = ShaderCollection.GetShaderCollection(filePath, package);
        using var contentFile = new ShaderExtract(collection).ToContentFile(vfxFileName);
        ExportContentFile(contentFile);

        return true;
    }

    private bool PrintNonResource(string contentPath, Stream stream)
    {
        using var contentFile = FileExtract.ExtractNonResource(stream, contentPath);

        if (contentFile != null)
        {
            PrintContentFile(contentFile);
            return true;
        }

        var data = new byte[stream.Length - stream.Position];
        stream.ReadExactly(data);
        PrintFile(contentPath, data);

        return true;
    }

    private void ExportNavMeshGltf(string path, string contentPath, Stream stream)
    {
        var navMeshFile = new NavMeshFile();
        navMeshFile.Read(stream);

        CreateGltfExporter(new NullFileLoader()).Export(navMeshFile, path, GetGltfOutputPath(contentPath));
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
