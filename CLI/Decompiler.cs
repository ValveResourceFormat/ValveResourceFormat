using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.TextureDecoders;
using ValveResourceFormat.ToolsAssetInfo;
using ValveResourceFormat.Utils;
using ValveResourceFormat.ValveFont;

namespace CLI
{
    public partial class Decompiler
    {
        private readonly Dictionary<string, ResourceStat> stats = [];
        private readonly Dictionary<string, string> uniqueSpecialDependencies = [];
        private readonly HashSet<string> unknownEntityKeys = [];
        private HashSet<string>? knownEntityKeys;

        private readonly Lock ConsoleWriterLock = new();
        private readonly StringBuilder ConsoleOutputBuilder = new(1024);
        private int CurrentFile;
        private int TotalFiles;

        // Options
        private string InputFile = string.Empty; // Should be non empty by the time input args are validated
        private string? OutputFile;
        private bool RecursiveSearch;
        private bool RecursiveSearchArchives;
        private bool PrintAllBlocks;
        private string? BlockToPrint;
        private bool ShouldPrintBlockContents => PrintAllBlocks || !string.IsNullOrEmpty(BlockToPrint);
        private int MaxParallelismThreads;
        private bool OutputVPKDir;
        private bool VerifyVPKChecksums;
        private bool CachedManifest;
        private bool Decompile;
        private TextureCodec TextureDecodeFlags;
        private string[] FileFilter = [];
        private bool ListResources;
        private string? GltfExportFormat;
        private bool GltfExportAnimations;
        private string[] GltfAnimationFilter = [];
        private bool GltfExportMaterials;
        private bool GltfExportAdaptTextures;
        private bool GltfExportExtras;
        private bool ToolsAssetInfoShort;

        // The options below are for collecting stats and testing exporting, this is mostly intended for VRF developers, not end users.
        private bool CollectStats;
        private bool StatsPrintFilePaths;
        private bool StatsPrintUniqueDependencies;
        private bool StatsCollectParticles;
        private bool StatsCollectVBIB;
        private bool GltfTest;
        private bool DumpUnknownEntityKeys;

        private string[]? ExtFilterList;
        private bool IsInputFolder;
        private Progress<string>? ProgressReporter;

        public static void Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            var decompiler = new Decompiler();

            ConsoleApp.Version = GetVersion();

            // https://github.com/Cysharp/ConsoleAppFramework
            // Go to definition on this method to see the generated source code
            ConsoleApp.Run(args, decompiler.HandleArguments);
        }

        /// <summary>
        /// A test bed command line interface for the VRF library.
        /// </summary>
        /// <param name="input">-i, Input file to be processed. With no additional arguments, a summary of the input(s) will be displayed.</param>
        /// <param name="output">-o, Output path to write to. If input is a folder (or a VPK), this should be a folder.</param>
        /// <param name="decompile">-d|--vpk_decompile, Decompile supported resource files.</param>
        /// <param name="texture_decode_flags">Decompile textures with the specified decode flags, example: "none", "auto", "foceldr".</param>
        /// <param name="recursive">If specified and given input is a folder, all sub directories will be scanned too.</param>
        /// <param name="recursive_vpk">If specified along with --recursive, will also recurse into VPK archives.</param>
        /// <param name="all">-a, Print the content of each resource block in the file.</param>
        /// <param name="block">-b, Print the content of a specific block, example: DATA, RERL, REDI, NTRO.</param>
        /// <param name="threads">If higher than 1, files will be processed concurrently.</param>
        /// <param name="vpk_dir">Print a list of files in given VPK and information about them.</param>
        /// <param name="vpk_verify">Verify checksums and signatures.</param>
        /// <param name="vpk_cache">Use cached VPK manifest to keep track of updates. Only changed files will be written to disk.</param>
        /// <param name="vpk_extensions">-e, File extension(s) filter, example: "vcss_c,vjs_c,vxml_c".</param>
        /// <param name="vpk_filepath">-f, File path filter, example: "panorama/,sounds/" or "scripts/items/items_game.txt".</param>
        /// <param name="vpk_list">-l, Lists all resources in given VPK. File extension and path filters apply.</param>
        /// <param name="gltf_export_format">Exports meshes/models in given glTF format. Must be either "gltf" or "glb".</param>
        /// <param name="gltf_export_animations">Whether to export model animations during glTF exports.</param>
        /// <param name="gltf_animation_list">Animations to include in the glTF, example "idle,dropped". By default will include all animations.</param>
        /// <param name="gltf_export_materials">Whether to export materials during glTF exports.</param>
        /// <param name="gltf_textures_adapt">Whether to perform any glTF spec adaptations on textures (e.g. split metallic map).</param>
        /// <param name="gltf_export_extras">Export additional Mesh properties into glTF extras</param>
        /// <param name="tools_asset_info_short">Whether to print only file paths for tools_asset_info files.</param>
        /// <param name="stats">Collect stats on all input files and then print them. Use "-i steam" to scan all Steam libraries.</param>
        /// <param name="stats_print_files">When using --stats, print example file names for each stat.</param>
        /// <param name="stats_unique_deps">When using --stats, print all unique dependencies that were found.</param>
        /// <param name="stats_particles">When using --stats, collect particle operators, renderers, emitters, initializers.</param>
        /// <param name="stats_vbib">When using --stats, collect vertex attributes.</param>
        /// <param name="gltf_test">When using --stats, also test glTF export code path for every supported file.</param>
        /// <param name="dump_unknown_entity_keys">When using --stats, save all unknown entity key hashes to unknown_keys.txt.</param>
        private int HandleArguments(
            string input,
            string? output = default,
            bool decompile = false,
            string texture_decode_flags = nameof(TextureCodec.Auto),
            bool recursive = false,
            bool recursive_vpk = false,
            bool all = false,
            string? block = default,
            int threads = 1,
            bool vpk_dir = false,
            bool vpk_verify = false,
            bool vpk_cache = false,
            string? vpk_extensions = default,
            string? vpk_filepath = default,
            bool vpk_list = false,

            string? gltf_export_format = default,
            bool gltf_export_animations = false,
            string? gltf_animation_list = default,
            bool gltf_export_materials = false,
            bool gltf_textures_adapt = false,
            bool gltf_export_extras = false,
            bool tools_asset_info_short = false,

            bool stats = false,
            bool stats_print_files = false,
            bool stats_unique_deps = false,
            bool stats_particles = false,
            bool stats_vbib = false,
            bool gltf_test = false,
            bool dump_unknown_entity_keys = false
        )
        {
            // When you modify the arguments, don't forget to update the command-line.md documentation file too.
            InputFile = stats && input.Equals("steam", StringComparison.OrdinalIgnoreCase) ? "steam" : Path.GetFullPath(input);
            OutputFile = output;
            Decompile = decompile;
            TextureDecodeFlags = Enum.Parse<TextureCodec>(texture_decode_flags, true);
            RecursiveSearch = recursive;
            RecursiveSearchArchives = recursive_vpk;
            PrintAllBlocks = all;
            BlockToPrint = block;
            MaxParallelismThreads = threads;
            OutputVPKDir = vpk_dir;
            VerifyVPKChecksums = vpk_verify;
            CachedManifest = vpk_cache;
            FileFilter = vpk_filepath?.Split(',') ?? [];
            ListResources = vpk_list;

            GltfExportFormat = gltf_export_format;
            GltfExportMaterials = gltf_export_materials;
            GltfExportAnimations = gltf_export_animations;
            GltfAnimationFilter = gltf_animation_list?.Split(',') ?? [];
            GltfExportAdaptTextures = gltf_textures_adapt;
            GltfExportExtras = gltf_export_extras;
            ToolsAssetInfoShort = tools_asset_info_short;

            CollectStats = stats;
            StatsPrintFilePaths = stats_print_files;
            StatsPrintUniqueDependencies = stats_unique_deps;
            StatsCollectParticles = stats_particles;
            StatsCollectVBIB = stats_vbib;
            GltfTest = gltf_test;
            DumpUnknownEntityKeys = dump_unknown_entity_keys;

            if (OutputFile != null)
            {
                OutputFile = Path.GetFullPath(OutputFile);
                OutputFile = FixPathSlashes(OutputFile);
            }

            for (var i = 0; i < FileFilter.Length; i++)
            {
                FileFilter[i] = FixPathSlashes(FileFilter[i]);
            }

            if (vpk_extensions != null)
            {
                ExtFilterList = vpk_extensions.Split(',');
            }

            if (GltfExportFormat is not "gltf" and not "glb" and not null)
            {
                Console.Error.WriteLine("glTF export format must be either 'gltf' or 'glb'.");
                return 1;
            }

            if (!GltfExportAnimations && GltfAnimationFilter.Length > 0)
            {
                Console.Error.WriteLine("glTF animation filter is only valid when exporting animations.");
                return 1;
            }

            if (CollectStats && OutputFile != null)
            {
                Console.Error.WriteLine("Do not use --stats with --output.");
                return 1;
            }

            if (!Decompile && (GltfExportFormat != null || GltfExportAnimations || GltfExportMaterials || GltfExportAdaptTextures || GltfExportExtras))
            {
                Console.Error.WriteLine("Exporting to glTF requires specifying -d argument.");
                return 1;
            }

            return Execute();
        }

        private int Execute()
        {
            var paths = new List<string>();

            if (Directory.Exists(InputFile))
            {
                if (OutputFile != null && File.Exists(OutputFile))
                {
                    Console.Error.WriteLine("Output path is an existing file, but input is a folder.");

                    return 1;
                }

                // Make sure we always have a trailing slash for input folders
                if (!InputFile.EndsWith(Path.DirectorySeparatorChar))
                {
                    InputFile += Path.DirectorySeparatorChar;
                }

                IsInputFolder = true;

                var dirs = FindPathsToProcessInFolder(InputFile);

                if (dirs == null)
                {
                    return 1;
                }

                paths.AddRange(dirs);
            }
            else if (File.Exists(InputFile))
            {
                if (RecursiveSearch)
                {
                    Console.Error.WriteLine("File passed in with --recursive option. Either pass in a folder or remove --recursive.");

                    return 1;
                }

                // TODO: Support recursing vpks inside of vpk?
                if (RecursiveSearchArchives && !CollectStats)
                {
                    Console.Error.WriteLine("File passed in with --recursive_vpk option, this is not supported.");

                    return 1;
                }

                paths.Add(InputFile);
            }
            else if (InputFile == "steam")
            {
                IsInputFolder = true;

                var steamPaths = GameFolderLocator.FindSteamLibraryFolderPaths();

                foreach (var path in steamPaths)
                {
                    var filesInPath = FindPathsToProcessInFolder(path);

                    if (filesInPath != null)
                    {
                        paths.AddRange(filesInPath);
                    }
                }

                if (paths.Count == 0)
                {
                    Console.Error.WriteLine("Did not find any Steam libraries.");
                    return 1;
                }
            }
            else if (CollectStats && !string.IsNullOrEmpty(InputFile)) // TODO: Support multiple paths for non --stats too
            {
                var splitPaths = InputFile.Split(',');

                IsInputFolder = true;

                foreach (var path in splitPaths)
                {
                    if (!Directory.Exists(path))
                    {
                        Console.Error.WriteLine($"Folder \"{path}\" does not exist.");
                        return 1;
                    }

                    var filesInPath = FindPathsToProcessInFolder(path);

                    if (filesInPath != null)
                    {
                        paths.AddRange(filesInPath);
                    }
                }
            }
            else
            {
                Console.Error.WriteLine("Input \"{0}\" is not a file or a folder.", InputFile);

                return 1;
            }

            CurrentFile = 0;
            TotalFiles = paths.Count;

            ProgressReporter = new Progress<string>(progress => Console.WriteLine($"--- {progress}"));

            if (MaxParallelismThreads > 1)
            {
                Console.WriteLine("Will use {0} threads concurrently.", MaxParallelismThreads);

                var queue = new ConcurrentQueue<string>(paths);
                var tasks = new List<Task>();

                ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);

                if (workerThreads < MaxParallelismThreads)
                {
                    ThreadPool.SetMinThreads(MaxParallelismThreads, MaxParallelismThreads);
                }

                for (var n = 0; n < MaxParallelismThreads; n++)
                {
                    tasks.Add(Task.Run(() =>
                    {
                        while (queue.TryDequeue(out var path))
                        {
                            ProcessFile(path);
                        }
                    }));
                }

                Task.WhenAll(tasks).GetAwaiter().GetResult();
            }
            else
            {
                foreach (var path in paths)
                {
                    ProcessFile(path);
                }
            }

            if (CollectStats)
            {
                Console.WriteLine();
                Console.WriteLine($"Processed {CurrentFile} resources:");

                foreach (var stat in stats.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key))
                {
                    var info = string.IsNullOrEmpty(stat.Value.Info) ? string.Empty : $" ({stat.Value.Info})";

                    Console.WriteLine($"{stat.Value.Count,5} resources of version {stat.Value.Version} and type {stat.Value.Type}{info}");

                    if (StatsPrintFilePaths)
                    {
                        foreach (var file in stat.Value.FilePaths)
                        {
                            Console.WriteLine($"\t\t{file}");
                        }
                    }
                }

                if (StatsPrintUniqueDependencies)
                {
                    Console.WriteLine();
                    Console.WriteLine("Unique special dependencies:");

                    foreach (var stat in uniqueSpecialDependencies)
                    {
                        Console.WriteLine($"{stat.Key} in {stat.Value}");
                    }
                }
            }

            if (DumpUnknownEntityKeys && unknownEntityKeys.Count > 0)
            {
                File.WriteAllLines("unknown_keys.txt", unknownEntityKeys.Select(x => x.ToString(CultureInfo.InvariantCulture)));
                Console.WriteLine($"Wrote {unknownEntityKeys.Count} unknown entity keys to unknown_keys.txt");
            }

            return 0;
        }

        private List<string>? FindPathsToProcessInFolder(string path)
        {
            var paths = Directory
                .EnumerateFiles(path, "*.*", RecursiveSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(s =>
                {
                    if (ExtFilterList != null)
                    {
                        foreach (var ext in ExtFilterList)
                        {
                            if (s.EndsWith(ext, StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }

                        return false;
                    }

                    return SupportedFileNamesRegex().IsMatch(s);
                })
                .ToList();

            if (RecursiveSearchArchives)
            {
                if (!RecursiveSearch)
                {
                    Console.Error.WriteLine("Option --recursive_vpk must be specified with --recursive.");

                    return null;
                }

                var vpkRegex = VpkArchiveIndexRegex();
                var vpks = Directory
                    .EnumerateFiles(path, "*.vpk", SearchOption.AllDirectories)
                    .Where(s => !vpkRegex.IsMatch(s));

                paths.AddRange(vpks);
            }

            if (paths.Count == 0)
            {
                Console.Error.WriteLine($"Unable to find any \"_c\" compiled files in \"{path}\" folder.");

                if (!RecursiveSearch)
                {
                    Console.Error.WriteLine("Perhaps you should specify --recursive option to scan the input folder recursively.");
                }

                return null;
            }

            return paths;
        }

        private void ProcessFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            ProcessFile(path, fs);
        }

        private void ProcessFile(string path, Stream stream, string? originalPath = null)
        {
            lock (ConsoleWriterLock)
            {
                CurrentFile++;

                if (ListResources)
                {
                    // do not print a header
                }
                else if (CollectStats && RecursiveSearch)
                {
                    if (CurrentFile % 1000 == 0)
                    {
                        Console.WriteLine($"Processing file {CurrentFile} out of {TotalFiles} files - {path}");
                    }
                }
                else if (IsInputFolder || originalPath != null)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write($"[{CurrentFile}/{TotalFiles}] ");

                    if (originalPath != null)
                    {
                        if (IsInputFolder && originalPath.StartsWith(InputFile, StringComparison.Ordinal))
                        {
                            Console.Write(originalPath[InputFile.Length..]);
                            Console.Write(" -> ");
                        }
                        else if (originalPath != InputFile)
                        {
                            Console.Write(originalPath);
                            Console.Write(" -> ");
                        }
                    }

                    Console.WriteLine(path);
                    Console.ResetColor();
                }
            }

            Span<byte> magicData = stackalloc byte[4];

            if (stream.Length >= magicData.Length)
            {
                stream.ReadExactly(magicData);
                stream.Seek(-magicData.Length, SeekOrigin.Current);
            }

            var magic = BitConverter.ToUInt32(magicData);

            switch (magic)
            {
                case Package.MAGIC: ParseVPK(path, stream); return;
                case VfxProgramData.MAGIC: ParseVCS(path, stream, originalPath); return;
                case NavMeshFile.MAGIC: ParseNAV(path, stream, originalPath); return;
                case ToolsAssetInfo.MAGIC2:
                case ToolsAssetInfo.MAGIC: ParseToolsAssetInfo(path, stream); return;
            }

            // Other types may be handled by FileExtract.TryExtractNonResource
            // TODO: Perhaps move nav into it too

            if (BinaryKV3.IsBinaryKV3(magic))
            {
                ParseKV3(path, stream);
                return;
            }

            var pathExtension = Path.GetExtension(path);

            const uint Source1Vcs = 0x06;
            if (CollectStats && pathExtension == ".vcs" && magic == Source1Vcs)
            {
                return;
            }

            if (pathExtension == ".vfont")
            {
                ParseVFont(path);

                return;
            }
            else if (pathExtension == ".uifont")
            {
                ParseUIFont(path);

                return;
            }
            else if (FileExtract.TryExtractNonResource(stream, path, out var content))
            {
                if (OutputFile != null)
                {
                    var extension = Path.GetExtension(content.FileName);
                    path = Path.ChangeExtension(path, extension);

                    var outFilePath = GetOutputPath(path);
                    DumpContentFile(outFilePath, content);
                }
                else
                {
                    var output = Encoding.UTF8.GetString(content.Data);

                    if (!CollectStats)
                    {
                        Console.WriteLine(output);
                    }
                }
                content.Dispose();

                return;
            }

            using var resource = new Resource
            {
                FileName = path,
            };

            try
            {
                resource.Read(stream);

                var extension = FileExtract.GetExtension(resource);

                if (extension == null)
                {
                    extension = Path.GetExtension(path);

                    if (extension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                    {
                        extension = extension[..^2];
                    }
                }

                if (CollectStats)
                {
                    TestAndCollectStats(resource, path, originalPath);
                }

                if (OutputFile != null)
                {
                    using var fileLoader = new GameFileLoader(null, resource.FileName);

                    path = Path.ChangeExtension(path, extension);
                    var outFilePath = GetOutputPath(path);

                    if (GltfExportFormat != null && GltfModelExporter.CanExport(resource))
                    {
                        outFilePath = Path.ChangeExtension(outFilePath, GltfExportFormat);
                        Directory.CreateDirectory(Path.GetDirectoryName(outFilePath)!);

                        CreateGltfExporter(fileLoader).Export(resource, outFilePath);
                        return;
                    }

                    using var contentFile = DecompileResource(resource, fileLoader);

                    var extensionNew = Path.GetExtension(outFilePath);
                    if (extensionNew.Length == 0 || extensionNew[1..] != extension)
                    {
                        lock (ConsoleWriterLock)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Extension '.{extension}' might be more suitable than the one provided '{extensionNew}'");
                            Console.ResetColor();
                        }
                    }

                    DumpContentFile(outFilePath, contentFile);
                    return;
                }
            }
            catch (Exception e)
            {
                LogException(e, path, originalPath);
            }

            if (CollectStats)
            {
                return;
            }

            //Console.WriteLine("\tInput Path: \"{0}\"", args[fi]);
            //Console.WriteLine("\tResource Name: \"{0}\"", "???");
            //Console.WriteLine("\tID: {0:x16}", 0);

            lock (ConsoleWriterLock)
            {
                // Highlight resource type line if undetermined
                if (resource.ResourceType == ResourceType.Unknown)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                }

                Console.WriteLine("\tResource Type: {0} [Version {1}] [Header Version: {2}]", resource.ResourceType, resource.Version, resource.HeaderVersion);
                Console.ResetColor();
            }

            Console.WriteLine("\tFile Size: {0} bytes", resource.FileSize);
            Console.WriteLine(Environment.NewLine);

            var rerl = resource.ExternalReferences;
            if (rerl != null)
            {
                Console.WriteLine("--- Resource External Refs: ---");
                Console.WriteLine("\t{0,-16}  {1,-48}", "Id:", "Resource Name:");

                foreach (var res in rerl.ResourceRefInfoList)
                {
                    Console.WriteLine("\t{0:X16}  {1,-48}", res.Id, res.Name);
                }
            }
            else
            {
                Console.WriteLine("--- (No External Resource References Found)");
            }

            Console.WriteLine(Environment.NewLine);

            Console.WriteLine("--- Resource Blocks: Count {0} ---", resource.Blocks.Count);

            foreach (var block in resource.Blocks)
            {
                Console.WriteLine("\t-- Block: {0,-4}  Size: {1,-6} bytes [Offset: {2,6}]", block.Type, block.Size, block.Offset);
            }

            if (ShouldPrintBlockContents)
            {
                Console.WriteLine(Environment.NewLine);

                foreach (var block in resource.Blocks)
                {
                    if (!PrintAllBlocks && BlockToPrint != block.Type.ToString())
                    {
                        continue;
                    }

                    Console.WriteLine("--- Data for block \"{0}\" ---", block.Type);

                    lock (ConsoleWriterLock)
                    {
                        using var stringWriter = new ConsoleStringWriter(ConsoleOutputBuilder, CultureInfo.InvariantCulture);
                        using var writer = new IndentedTextWriter(stringWriter);
                        block.WriteText(writer);
                        writer.Flush();
                        Console.WriteLine();
                    }
                }
            }
        }

        private ContentFile DecompileResource(Resource resource, IFileLoader fileLoader)
        {
            return resource.ResourceType switch
            {
                ResourceType.Texture => new TextureExtract(resource)
                {
                    DecodeFlags = TextureDecodeFlags,
                }.ToContentFile(),
                _ => FileExtract.Extract(resource, fileLoader, ProgressReporter),
            };
        }

        private void ParseVCS(string path, Stream stream, string? originalPath)
        {
            using var shader = new VfxProgramData();

            try
            {
                shader.Read(path, stream);

                using var output = new IndentedTextWriter();

                if (!CollectStats)
                {
                    shader.PrintSummary(output);
                    Console.Write(output.ToString());
                }
                else
                {
                    shader.PrintSummary(output);

                    foreach (var zframe in shader.StaticComboEntries)
                    {
                        zframe.Value.Unserialize();
                    }

                    // Shader resources already store stats
                    if (shader.Resource == null)
                    {
                        var id = $"Binary shader version {shader.VcsVersion}";

                        if (originalPath != null)
                        {
                            path = $"{originalPath} -> {path}";
                        }

                        AddStat(id, id, path);
                    }
                }
            }
            catch (Exception e)
            {
                LogException(e, path, originalPath);
            }
        }

        private void ParseNAV(string path, Stream stream, string? originalPath)
        {
            try
            {
                var navMeshFile = new NavMeshFile();
                navMeshFile.Read(stream);

                if (!CollectStats)
                {
                    Console.WriteLine(navMeshFile.ToString());
                }
                else
                {
                    navMeshFile.ToString();

                    var id = $"NavMesh version {navMeshFile.Version}, subversion {navMeshFile.SubVersion}";

                    if (originalPath != null)
                    {
                        path = $"{originalPath} -> {path}";
                    }

                    AddStat(id, id, path);
                }
            }
            catch (Exception e)
            {
                LogException(e, path, originalPath);
            }
        }

        private void AddStat(string key, string info, string path, Resource? resource = null)
        {
            lock (stats)
            {
                if (stats.TryGetValue(key, out var existingStat))
                {
                    if (existingStat.Count++ < 10)
                    {
                        existingStat.FilePaths.Add(path);
                    }
                }
                else
                {
                    if (resource == null)
                    {
                        stats.Add(key, new ResourceStat(info, path));
                    }
                    else
                    {
                        stats.Add(key, new ResourceStat(resource, info, path));
                    }
                }
            }
        }

        private void ParseVFont(string path) // TODO: Accept Stream
        {
            var font = new ValveFont();

            try
            {
                var output = font.Read(path);

                if (OutputFile != null)
                {
                    path = Path.ChangeExtension(path, "ttf");
                    path = GetOutputPath(path);

                    DumpFile(path, output);
                }
            }
            catch (Exception e)
            {
                LogException(e, path);
            }
        }

        private void ParseUIFont(string path) // TODO: Accept Stream
        {
            var fontPackage = new UIFontFilePackage();

            try
            {
                fontPackage.Read(path);

                if (OutputFile != null)
                {
                    var outputDirectory = Path.GetDirectoryName(path)!;

                    foreach (var fontFile in fontPackage.FontFiles)
                    {
                        var outputPath = Path.Combine(outputDirectory, fontFile.FileName);
                        DumpFile(outputPath, fontFile.OpenTypeFontData);
                    }
                }
            }
            catch (Exception e)
            {
                LogException(e, path);
            }
        }

        private void ParseKV3(string path, Stream stream)
        {
            var kv3 = new BinaryKV3();

            try
            {
                using (var binaryReader = new BinaryReader(stream))
                {
                    kv3.Size = (uint)stream.Length;
                    kv3.Read(binaryReader);
                }

                Console.WriteLine(kv3.ToString());
            }
            catch (Exception e)
            {
                LogException(e, path);
            }
        }

        private void ParseVPK(string path, Stream stream)
        {
            using var package = new Package();
            package.SetFileName(path);

            try
            {
                package.Read(stream);
            }
            catch (NotSupportedException e)
            {
                lock (ConsoleWriterLock)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Error.WriteLine($"Failed to open vpk '{path}' - {e.Message}");
                    Console.ResetColor();
                }

                return;
            }
            catch (Exception e)
            {
                LogException(e, path);

                return;
            }

            if (VerifyVPKChecksums)
            {
                try
                {
                    VerifyVPK(package);
                }
                catch (Exception e)
                {
                    LogException(e, path);
                }

                return;
            }

            if (OutputFile == null)
            {
                Debug.Assert(package.Entries != null);

                var orderedEntries = package.Entries.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key).ToList();

                if (ExtFilterList != null)
                {
                    orderedEntries = orderedEntries.Where(x => ExtFilterList.Contains(x.Key)).ToList();
                }
                else if (CollectStats)
                {
                    orderedEntries = orderedEntries.Where(x =>
                    {
                        if (x.Key == "vpk")
                        {
                            return RecursiveSearchArchives;
                        }

                        return SupportedFileNamesRegex().IsMatch(x.Key);
                    }).ToList();
                }

                if (ListResources)
                {
                    var listEntries = orderedEntries.SelectMany(x => x.Value).ToList();
                    listEntries.Sort((a, b) => string.CompareOrdinal(a.GetFullPath(), b.GetFullPath()));

                    foreach (var (entry, _) in FilteredEntries(listEntries))
                    {
                        Console.WriteLine($"{entry.GetFullPath()} CRC:{entry.CRC32:x10} size:{entry.TotalLength}");
                    }

                    return;
                }

                if (!CollectStats)
                {
                    Console.WriteLine("--- Files in package:");
                }

                var processVpkFiles = CollectStats || ShouldPrintBlockContents;

                if (processVpkFiles)
                {
                    var queue = new ConcurrentQueue<PackageEntry>();

                    foreach (var entryGroup in orderedEntries)
                    {
                        foreach (var (entry, _) in FilteredEntries(entryGroup.Value))
                        {
                            queue.Enqueue(entry);
                        }
                    }

                    Interlocked.Add(ref TotalFiles, queue.Count);

                    if (MaxParallelismThreads > 1)
                    {
                        var tasks = new List<Task>();

                        for (var n = 0; n < MaxParallelismThreads; n++)
                        {
                            tasks.Add(Task.Run(() =>
                            {
                                while (queue.TryDequeue(out var file))
                                {
                                    using var entryStream = GameFileLoader.GetPackageEntryStream(package, file);
                                    ProcessFile(file.GetFullPath(), entryStream, path);
                                }
                            }));
                        }

                        Task.WhenAll(tasks).GetAwaiter().GetResult();
                    }
                    else
                    {
                        while (queue.TryDequeue(out var file))
                        {
                            package.ReadEntry(file, out var output);

                            using var entryStream = new MemoryStream(output);
                            ProcessFile(file.GetFullPath(), entryStream, path);
                        }
                    }
                }
                else
                {
                    foreach (var entry in orderedEntries)
                    {
                        Console.WriteLine($"\t{entry.Key}: {entry.Value.Count} files");
                    }
                }
            }
            else
            {
                Console.WriteLine("--- Dumping decompiled files...");

                const string CachedManifestVersionPrefix = "// s2v_version=";
                var manifestPath = string.Concat(path, ".manifest.txt");
                var manifestData = new Dictionary<string, uint>();

                if (CachedManifest && File.Exists(manifestPath))
                {
                    using var file = new StreamReader(manifestPath);
                    string? line;
                    var firstLine = true;
                    var goodCachedVersion = false;

                    // add version
                    while ((line = file.ReadLine()) != null)
                    {
                        var lineSpan = line.AsSpan();

                        if (firstLine)
                        {
                            firstLine = false;

                            if (lineSpan.StartsWith(CachedManifestVersionPrefix))
                            {
                                var oldVersion = lineSpan[CachedManifestVersionPrefix.Length..];
                                var newVersion = typeof(Decompiler).Assembly.GetName().Version!.ToString();

                                goodCachedVersion = oldVersion.SequenceEqual(newVersion);

                                if (!goodCachedVersion)
                                {
                                    break;
                                }
                            }
                        }

                        var space = lineSpan.IndexOf(' ');

                        if (space > 0 && uint.TryParse(lineSpan[..space], CultureInfo.InvariantCulture, out var hash))
                        {
                            manifestData.Add(lineSpan[(space + 1)..].ToString(), hash);
                        }
                    }

                    if (!goodCachedVersion)
                    {
                        Console.Error.WriteLine("Decompiler version changed, cached manifest will be ignored.");
                        manifestData.Clear();
                    }
                }

                using var fileLoader = new GameFileLoader(package, package.FileName);

                Debug.Assert(package.Entries != null);

                foreach (var type in package.Entries)
                {
                    ProcessVPKEntries(path, package, fileLoader, type.Key, manifestData);
                }

                if (CachedManifest)
                {
                    using var file = new StreamWriter(manifestPath);

                    file.WriteLine($"{CachedManifestVersionPrefix}{typeof(Decompiler).Assembly.GetName().Version}");

                    foreach (var hash in manifestData)
                    {
                        if (package.FindEntry(hash.Key) == null)
                        {
                            Console.WriteLine("\t{0} no longer exists in VPK", hash.Key);
                        }

                        file.WriteLine($"{hash.Value} {hash.Key}");
                    }
                }
            }

            if (OutputVPKDir)
            {
                foreach (var type in package.Entries)
                {
                    foreach (var file in type.Value)
                    {
                        Console.WriteLine(file);
                    }
                }
            }
        }

        private static void VerifyVPK(Package package)
        {
            if (!package.IsSignatureValid())
            {
                throw new InvalidDataException("The signature in this package is not valid.");
            }

            Console.WriteLine("Verifying hashes...");

            package.VerifyHashes();

            var processed = 0;
            var maximum = 1f;

            var progressReporter = new Progress<string>(progress =>
            {
                if (processed++ % 1000 == 0)
                {
                    Console.WriteLine($"[{processed / maximum * 100f,6:#00.00}%] {progress}");
                }
            });

            if (package.AccessPackFileHashes.Count > 0)
            {
                maximum = package.AccessPackFileHashes.Count;

                Console.WriteLine("Verifying chunk hashes...");

                package.VerifyChunkHashes(progressReporter);
            }
            else
            {
                Debug.Assert(package.Entries != null);

                maximum = package.Entries.Sum(x => x.Value.Count);

                Console.WriteLine("Verifying file checksums...");

                package.VerifyFileChecksums(progressReporter);
            }

            Console.WriteLine("Success.");
        }

        private void ProcessVPKEntries(string parentPath, Package package,
            IFileLoader fileLoader, string type, Dictionary<string, uint> manifestData)
        {
            if (ExtFilterList != null)
            {
                if (!ExtFilterList.Contains(type))
                {
                    return;
                }
            }

            Debug.Assert(package.Entries != null);

            if (!package.Entries.TryGetValue(type, out var entries))
            {
                Console.WriteLine("There are no files of type \"{0}\".", type);

                return;
            }

            var gltfExporter = CreateGltfExporter(fileLoader);

            foreach (var (file, filePath) in FilteredEntries(entries))
            {
                var extension = type;

                if (OutputFile != null && CachedManifest)
                {
                    if (manifestData.TryGetValue(filePath, out var oldCrc32) && oldCrc32 == file.CRC32)
                    {
                        continue;
                    }

                    manifestData[filePath] = file.CRC32;
                }

                Console.WriteLine("\t[archive index: {0:D3}] {1}", file.ArchiveIndex, filePath);

                var totalLength = (int)file.TotalLength;
                var rawFileData = ArrayPool<byte>.Shared.Rent(totalLength);
                ContentFile? contentFile = null;

                try
                {
                    package.ReadEntry(file, rawFileData);

                    // Not a file that can be decompiled, or no decompilation was requested
                    var isVcsFile = type == "vcs";

                    if (!Decompile || !type.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal) && !isVcsFile)
                    {
                        if (OutputFile != null)
                        {
                            var outputFile = filePath;

                            if (RecursiveSearchArchives)
                            {
                                outputFile = Path.Combine(parentPath, outputFile);
                            }

                            outputFile = GetOutputPath(outputFile, useOutputAsDirectory: true);

                            DumpFile(outputFile, rawFileData.AsSpan()[..totalLength]);
                        }

                        continue;
                    }

                    using var memory = new MemoryStream(rawFileData, 0, totalLength);

                    if (OutputFile != null)
                    {
                        string outputFile;
                        // VCS files require multiple files to be decompiled together
                        if (isVcsFile)
                        {
                            var fileName = Path.GetFileNameWithoutExtension(filePath);
                            // Only parse features files
                            if (!fileName.EndsWith("_features", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            var collection = ShaderCollection.GetShaderCollection(filePath, package);
                            contentFile = new ShaderExtract(collection).ToContentFile();

                            // Remove the last part from the file name ("_features", ...)
                            var newFileNameBase = fileName.AsSpan(0, fileName.LastIndexOf('_'));
                            var directory = Path.GetDirectoryName(filePath);

                            outputFile = Path.Combine(directory ?? string.Empty, string.Concat(newFileNameBase, ".vfx"));
                        }
                        else
                        {
                            using var resource = new Resource
                            {
                                FileName = filePath,
                            };
                            resource.Read(memory);

                            if (GltfExportFormat != null && GltfModelExporter.CanExport(resource))
                            {
                                var outputExtension = GltfExportFormat;
                                outputFile = Path.Combine(OutputFile, Path.ChangeExtension(filePath, outputExtension));

                                Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

                                gltfExporter.Export(resource, outputFile);

                                continue;
                            }

                            contentFile = DecompileResource(resource, fileLoader);

                            outputFile = filePath;
                            extension = FileExtract.GetExtension(resource) ?? type[..^2];
                            if (type != extension)
                            {
                                outputFile = Path.ChangeExtension(outputFile, extension);
                            }
                        }

                        if (RecursiveSearchArchives)
                        {
                            outputFile = Path.Combine(parentPath, outputFile);
                        }

                        outputFile = GetOutputPath(outputFile, useOutputAsDirectory: true);

                        DumpContentFile(outputFile, contentFile);
                    }
                }
                catch (Exception e)
                {
                    LogException(e, filePath, parentPath);
                }
                finally
                {
                    contentFile?.Dispose();
                    ArrayPool<byte>.Shared.Return(rawFileData);
                }
            }
        }

        private GltfModelExporter CreateGltfExporter(IFileLoader fileLoader)
        {
            Debug.Assert(ProgressReporter != null);

            var gltfModelExporter = new GltfModelExporter(fileLoader)
            {
                ExportAnimations = GltfExportAnimations,
                ExportMaterials = GltfExportMaterials,
                AdaptTextures = GltfExportAdaptTextures,
                ExportExtras = GltfExportExtras,
                ProgressReporter = ProgressReporter,
            };

            gltfModelExporter.AnimationFilter.UnionWith(GltfAnimationFilter);
            return gltfModelExporter;
        }

        private static void DumpContentFile(string path, ContentFile contentFile, bool dumpSubFiles = true)
        {
            if (contentFile.Data != null)
            {
                DumpFile(path, contentFile.Data);
            }

            foreach (var additionalFile in contentFile.AdditionalFiles)
            {
                DumpContentFile(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(additionalFile.FileName)), additionalFile);
            }

            if (dumpSubFiles)
            {
                foreach (var contentSubFile in contentFile.SubFiles)
                {
                    DumpFile(Path.Combine(Path.GetDirectoryName(path)!, contentSubFile.FileName), contentSubFile.Extract.Invoke());
                }
            }
        }

        private static void DumpFile(string path, ReadOnlySpan<byte> data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllBytes(path, data.ToArray());

            Console.WriteLine("--- Dump written to \"{0}\"", path);
        }

        private IEnumerable<(PackageEntry Entry, string FilePath)> FilteredEntries(IEnumerable<PackageEntry> entries)
        {
            foreach (var entry in entries)
            {
                var filePath = FixPathSlashes(entry.GetFullPath());

                if (IsExcludedVpkFilePath(filePath))
                {
                    continue;
                }

                yield return (entry, filePath);
            }
        }

        private bool IsExcludedVpkFilePath(string filePath)
        {
            return FileFilter.Length > 0 && FileFilter.All(filter => !filePath.StartsWith(filter, StringComparison.Ordinal));
        }

        private string GetOutputPath(string inputPath, bool useOutputAsDirectory = false)
        {
            Debug.Assert(OutputFile != null);

            if (IsInputFolder)
            {
                if (!inputPath.StartsWith(InputFile, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Path '{inputPath}' does not start with '{InputFile}', is this a bug?", nameof(inputPath));
                }

                inputPath = inputPath[InputFile.Length..];

                return Path.Combine(OutputFile, inputPath);
            }
            else if (useOutputAsDirectory || Directory.Exists(OutputFile))
            {
                return Path.Combine(OutputFile, inputPath);
            }

            return Path.GetFullPath(OutputFile);
        }

        /// <summary>
        /// This method tries to run through all the code paths for a particular resource,
        /// which allows us to quickly find exceptions when running --stats over an entire game folder.
        /// </summary>
        private void TestAndCollectStats(Resource resource, string path, string? originalPath)
        {
            if (originalPath != null)
            {
                path = $"{originalPath} -> {path}";
            }

            // The rest of this code gathers various statistics
            var id = $"{resource.ResourceType}_{resource.Version}";
            var info = string.Empty;

            void AddStatLocal(string info)
            {
                var key = string.IsNullOrEmpty(info) ? id : string.Concat(id, "_", info);
                AddStat(key, info, path, resource);
            }

            switch (resource.ResourceType)
            {
                case ResourceType.Texture:
                    var texture = (Texture?)resource.DataBlock;
                    Debug.Assert(texture != null);
                    info = texture.Format.ToString();
                    break;

                case ResourceType.Sound:
                    if (resource.DataBlock is Sound soundData)
                    {
                        info = soundData.SoundType.ToString();
                    }
                    else if (resource.GetBlockByType(BlockType.CTRL) is BinaryKV3 ctrlData)
                    {
                        info = ctrlData.Data.GetStringProperty("_class");
                    }

                    break;

                case ResourceType.EntityLump:
                    if (DumpUnknownEntityKeys)
                    {
                        var entityLump = (EntityLump?)resource.DataBlock;
                        Debug.Assert(entityLump != null);
                        var entities = entityLump.GetEntities();
                        knownEntityKeys ??= [.. EntityLumpKnownKeys.KnownKeys];

                        foreach (var entity in entities)
                        {
                            foreach (var property in entity.Properties)
                            {
                                if (!knownEntityKeys.Contains(property.Key))
                                {
                                    lock (unknownEntityKeys)
                                    {
                                        unknownEntityKeys.Add(property.Key["vrf_unknown_key_".Length..]);
                                    }
                                }
                            }
                        }
                    }
                    break;

                case ResourceType.Particle:
                    if (StatsCollectParticles)
                    {
                        var particleSystem = (ParticleSystem?)resource.DataBlock;
                        Debug.Assert(particleSystem != null);

                        foreach (var op in particleSystem.GetInitializers())
                        {
                            AddStatLocal($"Initializer: {op.GetProperty<string>("_class")}");
                        }

                        foreach (var op in particleSystem.GetRenderers())
                        {
                            AddStatLocal($"Renderer: {op.GetProperty<string>("_class")}");
                        }

                        foreach (var op in particleSystem.GetEmitters())
                        {
                            AddStatLocal($"Emitter: {op.GetProperty<string>("_class")}");
                        }

                        foreach (var op in particleSystem.GetOperators())
                        {
                            AddStatLocal($"Operator: {op.GetProperty<string>("_class")}");
                        }
                    }
                    break;

                case ResourceType.Model:
                    if (StatsCollectVBIB)
                    {
                        var model = (Model?)resource.DataBlock;
                        Debug.Assert(model != null);

                        foreach (var embedded in model.GetEmbeddedMeshes())
                        {
                            foreach (var buffer in embedded.Mesh.VBIB.VertexBuffers)
                            {
                                foreach (var attribute in buffer.InputLayoutFields)
                                {
                                    AddStatLocal($"Attribute {attribute.SemanticName} - Format {attribute.Format}");
                                }
                            }
                        }
                    }
                    break;

                case ResourceType.Mesh:
                    if (StatsCollectVBIB)
                    {
                        var mesh = (Mesh?)resource.DataBlock;
                        Debug.Assert(mesh != null);

                        foreach (var buffer in mesh.VBIB.VertexBuffers)
                        {
                            foreach (var attribute in buffer.InputLayoutFields)
                            {
                                AddStatLocal($"Attribute {attribute.SemanticName} - Format {attribute.Format}");
                            }
                        }
                    }
                    break;
                case ResourceType.Shader:
                    {
                        var stream = resource.Reader!.BaseStream;
                        stream.Seek(0, SeekOrigin.Begin);
                        ParseVCS(path, stream, originalPath);
                        break;
                    }
            }

            AddStatLocal(info);

            if (resource.EditInfo != null)
            {
                lock (uniqueSpecialDependencies)
                {
                    foreach (var dep in resource.EditInfo.SpecialDependencies)
                    {
                        uniqueSpecialDependencies[$"{dep.CompilerIdentifier} \"{dep.String}\""] = path;
                    }
                }
            }

            using var stringWriter = new NullStringWriter();
            using var writer = new IndentedTextWriter(stringWriter);

            foreach (var block in resource.Blocks)
            {
                block.WriteText(writer);
                stringWriter.GetStringBuilder().Clear();
            }

            InternalTestExtraction.Test(resource);

            if (GltfTest && GltfModelExporter.CanExport(resource) && resource.ResourceType != ResourceType.Map)
            {
                var gltfModelExporter = new GltfModelExporter(new NullFileLoader())
                {
                    ExportMaterials = false,
                    ExportExtras = GltfExportExtras,
                    ProgressReporter = new Progress<string>(progress => { }),
                };
                gltfModelExporter.Export(resource, null); // Filename passed as null which tells exporter to write gltf to a null stream
            }
        }

        private void LogException(Exception e, string path, string? parentPath = null)
        {
            var exceptionsFileName = CollectStats ? $"exceptions{Path.GetExtension(path)}.txt" : "exceptions.txt";

            lock (ConsoleWriterLock)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;

                if (parentPath == null)
                {
                    Console.Error.WriteLine($"File: {path}\n{e}");

                    File.AppendAllText(exceptionsFileName, $"---------------\nFile: {path}\nException: {e}\n\n");
                }
                else
                {
                    Console.Error.WriteLine($"File: {path} (parent: {parentPath})\n{e}");

                    File.AppendAllText(exceptionsFileName, $"---------------\nParent file: {parentPath}\nFile: {path}\nException: {e}\n\n");
                }

                Console.ResetColor();
            }
        }

        private static string FixPathSlashes(string path)
        {
            path = path.Replace('\\', '/');

            if (Path.DirectorySeparatorChar != '/')
            {
                path = path.Replace('/', Path.DirectorySeparatorChar);
            }

            return path;
        }

        private static string GetVersion()
        {
            var info = new StringBuilder();
            info.Append("Version: ");
            info.AppendLine(typeof(Decompiler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
            info.Append("OS: ");
            info.Append(RuntimeInformation.OSDescription);
            info.Append(" (");
            info.Append(RuntimeInformation.OSArchitecture.ToString());
            info.AppendLine(")");
            info.AppendLine("Website: https://valveresourceformat.github.io");
            info.Append("GitHub: https://github.com/ValveResourceFormat/ValveResourceFormat");
            return info.ToString();
        }

        [GeneratedRegex(
            @"(?:_c|\.vcs|\.nav|\.vfe|\.vfont|\.uifont)$|" +
            @"^(?:readonly_)?tools_asset_info\.bin$|" +
            @"^(?:subtitles|closecaption)_.*\.dat$"
        )]
        private static partial Regex SupportedFileNamesRegex();

        [GeneratedRegex(@"_[0-9]{3}\.vpk$")]
        private static partial Regex VpkArchiveIndexRegex();
    }
}
