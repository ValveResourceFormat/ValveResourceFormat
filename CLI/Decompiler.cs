using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ValvePak;
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
        private int FailedFiles;
        private int WrittenFiles;
        private bool LoggedExceptions;
        private bool AnyFileMatched;

        // Options
        private string InputFile = string.Empty; // Should be non empty by the time input args are validated
        private string? OutputFile;
        private bool OutputIsDirectory; // Otherwise the output path names the single file to write
        private bool RecursiveSearch;
        private bool RecursiveSearchArchives;
        private bool PrintAllBlocks;
        private HashSet<string> BlocksToPrint = [];
        private bool ShouldPrintBlockContents => PrintAllBlocks || BlocksToPrint.Count > 0;
        private int MaxParallelismThreads;
        private bool Quiet;
        private bool OutputToConsole;
        private ILogger FileLoaderLogger = NullLogger.Instance;
        private TextWriter Stdout = Console.Out;
        private bool OutputVPKDir;
        private bool VerifyVPKChecksums;
        private bool CachedManifest;
        private bool Decompile;
        private TextureCodec TextureDecodeFlags = TextureCodec.Auto;
        private string[] FileFilter = [];
        private string? LinkedFilePath; // The file inside of the package that a "vpk:" input link points to
        private bool HasPathFilter => FileFilter.Length > 0 || LinkedFilePath != null;
        private bool ListResources;
        private string? GamePath;
        private string? GltfExportFormat;
        private bool GltfExportAnimations;
        private string[] GltfAnimationFilter = [];
        private string[] GltfMeshFilter = [];
        private bool GltfExportMaterials;
        private bool GltfExportAdaptTextures;
        private bool GltfExportExtras;
        private bool GltfComposeAdditive;
        private bool ToolsAssetInfoShort;

        // The options below are for collecting stats and testing exporting, this is mostly intended for VRF developers, not end users.
        private bool CollectStats;
        private bool StatsWithLoader;
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
        /// Inspect, extract and decompile Source 2 files and VPK archives, see https://s2v.app/ValveResourceFormat/guides/command-line.html for examples.
        /// </summary>
        /// <param name="input">-i, Input file or folder to be processed, multiple can be comma-separated (not with --output). Accepts paths relative to an installed Steam app such as "steam:730/game/csgo", and "vpk:" links copied in Source 2 Viewer. With no other options, a summary of the input(s) is printed.</param>
        /// <param name="recursive">If the input is a folder, also scan its subfolders.</param>
        /// <param name="recursive_vpk">If the input is a folder, also process files inside of VPK archives in it.</param>
        /// <param name="vpk_extensions">-e, File extension(s) filter, example: "vcss_c,vjs_c,vxml_c".</param>
        /// <param name="vpk_filepath">-f, File path filter(s), matching the start of the path inside the VPK or relative to the input folder (case-insensitive), or the full path when using * and ? wildcards. Example: "panorama/,sounds/" or "*/entities/*".</param>
        /// <param name="vpk_cache">Use a cached VPK manifest to keep track of updates, only changed files are written to disk. Requires --output.</param>
        /// <param name="vpk_verify">Verify checksums and signatures of the given VPK, or of every VPK in the given folder.</param>
        /// <param name="output">-o, Output path to write to. Treated as a folder when it is an existing folder, ends with a path separator, or has no file extension, otherwise it names the file to write, which requires the input to be a single file or the filters to match one file. Use "-" to print decompiled files to the console instead.</param>
        /// <param name="all">-a, Print the content of each resource block in the file.</param>
        /// <param name="block">-b, Print the content of specific block(s), example: "DATA" or "RERL,RED2".</param>
        /// <param name="decompile">-d|--vpk_decompile, Decompile supported resource files. Requires --output.</param>
        /// <param name="texture_decode_flags">Decompile textures with the specified decode flags, example: "none", "auto" (default), "ForceLDR". Requires --output.</param>
        /// <param name="vpk_list">-l, List all files in the given VPK or folder. File extension and path filters apply.</param>
        /// <param name="vpk_dir">Same as --vpk_list, but also print the archive index, offset and metadata size of each file.</param>
        /// <param name="gltf_export_format">Export meshes and models in the given glTF format, "gltf" or "glb". Implies --vpk_decompile.</param>
        /// <param name="gltf_export_materials">Export materials during glTF exports.</param>
        /// <param name="gltf_export_animations">Export model animations during glTF exports.</param>
        /// <param name="gltf_mesh_list">Meshes to include in the glTF, example: "mesh1,mesh2". By default all meshes are included.</param>
        /// <param name="gltf_animation_list">Animations to include in the glTF, example: "idle,dropped". Implies --gltf_export_animations. By default all animations are included.</param>
        /// <param name="gltf_textures_adapt">Perform glTF spec adaptations on exported textures (e.g. split metallic map). Implies --gltf_export_materials.</param>
        /// <param name="gltf_export_extras">Export additional mesh properties into glTF extras.</param>
        /// <param name="gltf_compose_additive">Compose additive animations over the bind pose instead of exporting their delta tracks.</param>
        /// <param name="shader_list_combos">List every compiled variant of a shader with its combo values and bytecode hash. For a material, only the variants of its shader that the material selects.</param>
        /// <param name="shader_combo">Decompile the shader variant matching these combo values, example: "S_ALPHA_TEST=1,D_BLEND_WEIGHT_COUNT=4". A bare name means "=1", omitted combos stay at their minimum. For a material, the static combos it selects are used.</param>
        /// <param name="tools_asset_info_short">Print only file paths for tools_asset_info files.</param>
        /// <param name="threads">If higher than 1, files are processed concurrently. Only used with --output or --test.</param>
        /// <param name="quiet">-q, When writing to --output, only print errors and a summary. With the shader options, only print their output.</param>
        /// <param name="game">Path to a gameinfo.gi file, or the folder containing it, to load game search paths from, such as "steam:730/game/csgo". Useful when the input file is not located inside a game folder.</param>
        /// <param name="test">Run every input file through all of the decompile code paths to find exceptions, and print how many files of each type and version were found. Use "-i steam" to scan all Steam libraries.</param>
        /// <param name="test_loader">When using --test, use GameFileLoader to load dependencies.</param>
        /// <param name="test_print_files">When using --test, print example file names for each type.</param>
        /// <param name="test_unique_deps">When using --test, print all unique dependencies that were found.</param>
        /// <param name="test_particles">When using --test, collect particle operators, renderers, emitters, initializers.</param>
        /// <param name="test_vertex_attributes">When using --test, collect vertex attributes.</param>
        /// <param name="test_gltf">When using --test, also test glTF export code path for every supported file.</param>
        /// <param name="test_entity_keys">When using --test, save all unknown entity key hashes to unknown_keys.txt.</param>
        private int HandleArguments(
            string input,
            bool recursive = false,
            bool recursive_vpk = false,
            [HideDefaultValue] string? vpk_extensions = default,
            [HideDefaultValue] string? vpk_filepath = default,
            bool vpk_cache = false,
            bool vpk_verify = false,

            [HideDefaultValue] string? output = default,
            bool all = false,
            [HideDefaultValue] string? block = default,
            bool decompile = false,
            [HideDefaultValue] string? texture_decode_flags = default,
            bool vpk_list = false,
            bool vpk_dir = false,

            [HideDefaultValue] string? gltf_export_format = default,
            bool gltf_export_materials = false,
            bool gltf_export_animations = false,
            [HideDefaultValue] string? gltf_mesh_list = default,
            [HideDefaultValue] string? gltf_animation_list = default,
            bool gltf_textures_adapt = false,
            bool gltf_export_extras = false,
            bool gltf_compose_additive = false,
            bool shader_list_combos = false,
            [HideDefaultValue] string? shader_combo = default,
            bool tools_asset_info_short = false,

            int threads = 1,
            bool quiet = false,
            [HideDefaultValue] string? game = default,

            bool test = false,
            bool test_loader = false,
            bool test_print_files = false,
            bool test_unique_deps = false,
            bool test_particles = false,
            bool test_vertex_attributes = false,
            bool test_gltf = false,
            bool test_entity_keys = false
        )
        {
            // When you modify the arguments, don't forget to update the command-line.md documentation file too.
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.Error.WriteLine("--input is empty.");
                return 1;
            }

            // A "vpk:" link copied from Source 2 Viewer, which can point to a file or folder inside of the package
            if (VpkLink.IsVpkLink(input))
            {
                var (packagePaths, linkedPath) = VpkLink.Parse(input);

                if (packagePaths.Count > 1)
                {
                    Console.Error.WriteLine("The vpk: link points into a VPK inside of a VPK, which is not supported.");
                    return 1;
                }

                input = packagePaths.Count == 1 ? packagePaths[0] : linkedPath;

                if (packagePaths.Count == 1 && linkedPath.Length > 0)
                {
                    if (vpk_filepath != null)
                    {
                        Console.Error.WriteLine("--vpk_filepath can not be used with a vpk: link that points inside of the package.");
                        return 1;
                    }

                    // A folder is matched the same as --vpk_filepath, but a file has to match exactly
                    if (linkedPath.EndsWith('/'))
                    {
                        vpk_filepath = linkedPath;
                    }
                    else
                    {
                        LinkedFilePath = FixPathSlashes(linkedPath);
                    }
                }
            }

            // Paths can be relative to a Steam app, such as "steam:730/game/csgo"
            var inputs = input.Split(',');

            for (var i = 0; i < inputs.Length; i++)
            {
                if (!GameFolderLocator.TryResolveSteamAppPath(inputs[i].Trim(), out inputs[i], out var inputError))
                {
                    Console.Error.WriteLine(inputError);
                    return 1;
                }
            }

            input = string.Join(',', inputs);

            if (game != null && !GameFolderLocator.TryResolveSteamAppPath(game, out game, out var gameError))
            {
                Console.Error.WriteLine(gameError);
                return 1;
            }

            // Options that only make sense together with another one turn it on
            test |= test_loader || test_print_files || test_unique_deps || test_particles || test_vertex_attributes || test_gltf || test_entity_keys;
            decompile |= gltf_export_format != null || output == "-";
            gltf_export_animations |= gltf_animation_list != null;
            gltf_export_materials |= gltf_textures_adapt;

            var isSteamInput = input.Equals("steam", StringComparison.OrdinalIgnoreCase) && !Path.Exists(input);
            var hasFilters = vpk_extensions != null || vpk_filepath != null || LinkedFilePath != null;

            InputFile = isSteamInput ? "steam" : Path.GetFullPath(input);
            OutputFile = output;
            Decompile = decompile;
            RecursiveSearch = recursive;
            RecursiveSearchArchives = recursive_vpk;
            PrintAllBlocks = all;
            BlocksToPrint = block?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            MaxParallelismThreads = threads;
            Quiet = quiet;
            OutputVPKDir = vpk_dir;
            VerifyVPKChecksums = vpk_verify;
            CachedManifest = vpk_cache;
            ListResources = vpk_list || vpk_dir;

            // Paths inside of VPKs do not start with a slash
            FileFilter = vpk_filepath?.Split(',').Select(filter => FixPathSlashes(filter.TrimStart('/', '\\'))).ToArray() ?? [];

            // Extensions are matched without the leading dot
            ExtFilterList = vpk_extensions?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(ext => ext.TrimStart('.')).ToArray();

            GltfExportFormat = gltf_export_format;
            GltfExportMaterials = gltf_export_materials;
            GltfExportAnimations = gltf_export_animations;
            GltfAnimationFilter = gltf_animation_list?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
            GltfMeshFilter = gltf_mesh_list?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
            GltfExportAdaptTextures = gltf_textures_adapt;
            GltfExportExtras = gltf_export_extras;
            GltfComposeAdditive = gltf_compose_additive;
            ToolsAssetInfoShort = tools_asset_info_short;
            ShaderListCombos = shader_list_combos;
            ShaderCombo = shader_combo;

            CollectStats = test;
            StatsWithLoader = test_loader;
            StatsPrintFilePaths = test_print_files;
            StatsPrintUniqueDependencies = test_unique_deps;
            StatsCollectParticles = test_particles;
            StatsCollectVBIB = test_vertex_attributes;
            GltfTest = test_gltf;
            DumpUnknownEntityKeys = test_entity_keys;

            if (OutputFile == "-")
            {
                // Output paths are still computed to name the printed files, relative to the working directory
                OutputToConsole = true;
                OutputFile = Environment.CurrentDirectory + Path.DirectorySeparatorChar;
            }

            if (OutputFile != null)
            {
                OutputFile = Path.GetFullPath(OutputFile);
                OutputFile = FixPathSlashes(OutputFile);

                // A path that does not exist is a folder unless it has a file extension
                OutputIsDirectory = OutputFile.EndsWith(Path.DirectorySeparatorChar)
                    || Directory.Exists(OutputFile)
                    || !Path.HasExtension(OutputFile);
            }

            if (game != null)
            {
                if (Directory.Exists(game))
                {
                    game = Path.Join(game, "gameinfo.gi");
                }

                if (!File.Exists(game))
                {
                    Console.Error.WriteLine($"Gameinfo file \"{game}\" does not exist.");
                    return 1;
                }

                GamePath = Path.GetFullPath(game);
            }

            if (texture_decode_flags != null)
            {
                if (!Enum.TryParse(texture_decode_flags, ignoreCase: true, out TextureCodec decodeFlags))
                {
                    Console.Error.WriteLine($"Unknown texture decode flags \"{texture_decode_flags}\", use any of: {string.Join(", ", Enum.GetNames<TextureCodec>())}.");
                    return 1;
                }

                TextureDecodeFlags = decodeFlags;
            }

            var blockNames = Enum.GetNames<BlockType>().Where(name => name != nameof(BlockType.Undefined)).ToArray();
            var unknownBlock = BlocksToPrint.FirstOrDefault(name => !blockNames.Contains(name, StringComparer.OrdinalIgnoreCase));

            if (unknownBlock != null)
            {
                Console.Error.WriteLine($"Unknown block \"{unknownBlock}\", use any of: {string.Join(", ", blockNames)}.");
                return 1;
            }

            if (GltfExportFormat is not "gltf" and not "glb" and not null)
            {
                Console.Error.WriteLine("glTF export format must be either 'gltf' or 'glb'.");
                return 1;
            }

            bool[] modes = [OutputFile != null, ListResources, VerifyVPKChecksums, ShouldPrintBlockContents, CollectStats, HasShaderOptions];

            if (modes.Count(mode => mode) > 1)
            {
                Console.Error.WriteLine("Only one of --output, --vpk_list (or --vpk_dir), --vpk_verify, --block (or --all), --test, and the shader options can be used at a time.");
                return 1;
            }

            // Input
            var inputIsFolder = Directory.Exists(InputFile);
            var inputIsFile = File.Exists(InputFile);

            if (isSteamInput && !CollectStats)
            {
                Console.Error.WriteLine("--input steam is only supported with --test.");
                return 1;
            }

            if (inputIsFile && RecursiveSearchArchives && !CollectStats)
            {
                Console.Error.WriteLine("--recursive_vpk only applies to folders, VPKs inside of VPKs are not supported.");
                return 1;
            }

            if (hasFilters && VerifyVPKChecksums)
            {
                Console.Error.WriteLine("--vpk_verify checks whole packages, --vpk_extensions and --vpk_filepath do not apply to it.");
                return 1;
            }

            if (hasFilters && inputIsFile && !InputFile.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("--vpk_extensions and --vpk_filepath only apply to folders and VPK files, not to a single file.");
                return 1;
            }

            // Output
            if (OutputFile != null && !inputIsFolder && !inputIsFile && InputFile.Contains(',', StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Multiple inputs are not supported with --output.");
                return 1;
            }

            if (OutputFile != null && inputIsFolder && File.Exists(OutputFile))
            {
                Console.Error.WriteLine("Output path is an existing file, but input is a folder.");
                return 1;
            }

            if (OutputFile != null && inputIsFolder && !OutputIsDirectory)
            {
                Console.Error.WriteLine("Output path has a file extension, but input is a folder. End the output path with a path separator to write into a folder with that name.");
                return 1;
            }

            if (OutputFile == null && Decompile)
            {
                Console.Error.WriteLine("--vpk_decompile requires --output. Use --output - to print the decompiled files instead.");
                return 1;
            }

            if (OutputFile == null && texture_decode_flags != null)
            {
                Console.Error.WriteLine("--texture_decode_flags requires --output.");
                return 1;
            }

            if (OutputFile == null && CachedManifest)
            {
                Console.Error.WriteLine("--vpk_cache requires --output.");
                return 1;
            }

            if (OutputToConsole && (GltfExportFormat != null || CachedManifest))
            {
                Console.Error.WriteLine("--output - only prints decompiled files, use it without glTF exports or --vpk_cache.");
                return 1;
            }

            if (Quiet && OutputFile == null && !HasShaderOptions)
            {
                Console.Error.WriteLine("--quiet is only supported with --output or the shader options.");
                return 1;
            }

            // Type specific
            if (GltfExportFormat == null && (GltfExportAnimations || GltfExportMaterials || GltfExportAdaptTextures || GltfExportExtras || GltfComposeAdditive || GltfMeshFilter.Length > 0))
            {
                Console.Error.WriteLine("The --gltf_* options require --gltf_export_format.");
                return 1;
            }

            if (StatsWithLoader && MaxParallelismThreads > 1)
            {
                Console.Error.WriteLine("--threads does not currently work with --test_loader.");
                return 1;
            }

            // The shader options only apply to shaders and materials, so skip everything else in folders and packages
            if (HasShaderOptions && ExtFilterList == null)
            {
                ExtFilterList = ["vcs", "vmat_c"];
            }

            // Printing to the console is done one file at a time
            if (OutputFile == null && !CollectStats)
            {
                MaxParallelismThreads = 1;
            }

            FileLoaderLogger = new StderrLogger(Quiet ? LogLevel.Warning : LogLevel.Information);

            return Execute();
        }

        private int Execute()
        {
            var paths = new List<string>();

            if (Directory.Exists(InputFile))
            {
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
                if (VpkArchiveIndexRegex().IsMatch(InputFile))
                {
                    var fixedPackage = $"{InputFile.AsSpan()[..^8]}_dir.vpk";

                    if (File.Exists(fixedPackage))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Error.WriteLine($"Warning: Did you mean to specify \"{Path.GetFileName(fixedPackage)}\" instead of \"{Path.GetFileName(InputFile)}\"?");
                        Console.ResetColor();
                    }
                }

                paths.Add(InputFile);
            }
            else if (InputFile == "steam")
            {
                IsInputFolder = true;

                var steamPaths = GameFolderLocator.FindSteamLibraryFolderPaths();

                if (steamPaths.Count == 0)
                {
                    Console.Error.WriteLine("Did not find any Steam libraries.");
                    return 1;
                }

                foreach (var path in steamPaths)
                {
                    var filesInPath = FindPathsToProcessInFolder(path);

                    if (filesInPath != null)
                    {
                        paths.AddRange(filesInPath);
                    }
                }
            }
            else if (InputFile.Contains(',', StringComparison.Ordinal))
            {
                IsInputFolder = true;

                foreach (var splitPath in InputFile.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var path = Path.GetFullPath(splitPath);

                    if (File.Exists(path))
                    {
                        paths.Add(path);
                        continue;
                    }

                    if (!Directory.Exists(path))
                    {
                        Console.Error.WriteLine($"Input \"{path}\" is not a file or a folder.");
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

            // Folders without any files to process have already been reported
            if (paths.Count == 0)
            {
                return 1;
            }

            CurrentFile = 0;
            TotalFiles = paths.Count;

            ProgressReporter = Quiet ? null : new Progress<string>(progress => Console.WriteLine($"--- {progress}"));

            // Library code also writes to the console, so silence all of stdout while processing
            Stdout = Console.Out;

            if (Quiet)
            {
                Console.SetOut(TextWriter.Null);
            }
            else if (OutputToConsole)
            {
                // Keep stdout for the printed files only
                Console.SetOut(Console.Error);
            }

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

            Console.SetOut(Stdout);

            lock (ShaderFileLoaders)
            {
                foreach (var loader in ShaderFileLoaders.Values)
                {
                    loader.Dispose();
                }
            }

            if (OutputFile != null && !OutputToConsole)
            {
                Console.WriteLine($"--- Wrote {WrittenFiles} files");
            }

            if (!AnyFileMatched && !CollectStats && (HasPathFilter || ExtFilterList != null))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine(LinkedFilePath != null
                    ? $"The vpk: link points to \"{LinkedFilePath.Replace('\\', '/')}\", which does not exist in the package."
                    : "No files matched the given filters. --vpk_filepath matches the start of the path unless it contains * or ? wildcards, use --vpk_list to see all paths.");
                Console.ResetColor();
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

            if (FailedFiles > 0)
            {
                var exceptionsFileName = CollectStats ? "exceptions.<extension>.txt" : "exceptions.txt";
                var exceptionsHint = LoggedExceptions ? $", see \"{Path.Combine(Environment.CurrentDirectory, exceptionsFileName)}\"" : string.Empty;
                Console.Error.WriteLine($"{FailedFiles} file(s) failed to process{exceptionsHint}.");
                return 2;
            }

            return 0;
        }

        private List<string>? FindPathsToProcessInFolder(string path)
        {
            var vpkRegex = VpkArchiveIndexRegex();
            var vpks = Directory
                .EnumerateFiles(path, "*.vpk", RecursiveSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(s => !vpkRegex.IsMatch(s));

            if (VerifyVPKChecksums)
            {
                var packages = vpks.ToList();

                if (packages.Count == 0)
                {
                    Console.Error.WriteLine($"Unable to find any VPK files in \"{path}\" folder.");
                    return null;
                }

                return packages;
            }

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
                .Where(s => FileFilter.Length == 0 || !IsExcludedVpkFilePath(FixPathSlashes(Path.GetRelativePath(path, s))))
                .ToList();

            if (RecursiveSearchArchives)
            {
                paths.AddRange(vpks);
            }

            if (paths.Count == 0)
            {
                Console.Error.WriteLine($"Unable to find any \"_c\" compiled files in \"{path}\" folder.");

                if (!RecursiveSearchArchives && vpks.Any())
                {
                    Console.Error.WriteLine("The folder contains VPK files, specify --recursive_vpk to process the files inside of them.");
                }
                else if (!RecursiveSearch)
                {
                    Console.Error.WriteLine("Perhaps you should specify --recursive option to scan the input folder recursively.");
                }

                return null;
            }

            return paths;
        }

        private void PrintFileHeader(string path, string? originalPath)
        {
            lock (ConsoleWriterLock)
            {
                CurrentFile++;

                if (CollectStats && RecursiveSearch)
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

                    if (originalPath != null && originalPath != InputFile)
                    {
                        Console.Write(GetDisplayPath(originalPath));
                        Console.Write(" -> ");
                    }

                    Console.WriteLine(path);
                    Console.ResetColor();
                }
            }
        }

        private void ProcessFile(string path, IFileLoader? fileLoader = null)
        {
            // Packages in folders are found by their extension, so other files do not need to be opened to be listed
            if (ListResources && !path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
            {
                AnyFileMatched = true;
                Console.WriteLine(GetDisplayPath(path));
                return;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            ProcessFile(path, fs, null, fileLoader);
        }

        private void ProcessFile(string path, Stream stream, string? originalPath = null, IFileLoader? fileLoader = null)
        {
            Span<byte> magicData = stackalloc byte[4];

            if (stream.Length >= magicData.Length)
            {
                stream.ReadExactly(magicData);
                stream.Seek(-magicData.Length, SeekOrigin.Current);
            }

            var magic = BitConverter.ToUInt32(magicData);

            // Packages count and print themselves, because they may expand into the files inside of them
            if (magic == Package.MAGIC)
            {
                ParseVPK(path, stream);
                return;
            }

            AnyFileMatched = true;

            if (VerifyVPKChecksums)
            {
                ReportError($"\"{path}\" is not a VPK file, --vpk_verify only applies to those.");
                return;
            }

            PrintFileHeader(path, originalPath);

            var pathExtension = Path.GetExtension(path);

            if (HasShaderOptions)
            {
                // Newer shaders are stored as resources, parse them directly instead of as a generic resource
                if (pathExtension == ".vcs")
                {
                    ParseVCS(path, stream, originalPath);
                    return;
                }

                if (pathExtension != ".vmat_c")
                {
                    ReportError($"\"{path}\" is not a shader or a material, the shader options only apply to those.");
                    return;
                }
            }

            switch (magic)
            {
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
                    DumpContentFile(outFilePath, content, singleFileOutput: !OutputIsDirectory);
                }
                else
                {
                    if (content.Data != null)
                    {
                        var output = Encoding.UTF8.GetString(content.Data);

                        if (!CollectStats)
                        {
                            Console.WriteLine(output);
                        }
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

                if (HasShaderOptions && resource.DataBlock is Material material)
                {
                    ProcessMaterialShaderOptions(material, originalPath ?? Path.GetDirectoryName(path)!);
                    return;
                }

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
                    TestAndCollectStats(resource, path, originalPath, fileLoader);
                }

                if (OutputFile != null)
                {
                    using var outputFileLoader = CreateGameFileLoader(null, resource.FileName);

                    path = Path.ChangeExtension(path, extension);
                    var outFilePath = GetOutputPath(path);

                    if (GltfExportFormat != null && GltfModelExporter.CanExport(resource))
                    {
                        outFilePath = Path.ChangeExtension(outFilePath, GltfExportFormat);
                        Directory.CreateDirectory(Path.GetDirectoryName(outFilePath)!);

                        CreateGltfExporter(outputFileLoader).Export(resource, outFilePath);
                        Interlocked.Increment(ref WrittenFiles);

                        Console.WriteLine("--- Dump written to \"{0}\"", outFilePath);
                        return;
                    }

                    using var contentFile = DecompileResource(resource, outputFileLoader);

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

                    DumpContentFile(outFilePath, contentFile, singleFileOutput: !OutputIsDirectory);
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
                    if (!PrintAllBlocks && !BlocksToPrint.Contains(block.Type.ToString()))
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

                if (HasShaderOptions)
                {
                    ProcessShaderOptions(shader);
                    return;
                }

                using var output = new IndentedTextWriter();

                if (!CollectStats)
                {
                    shader.PrintSummary(output);
                    Console.Write(output.ToString());
                }
                else
                {
                    shader.PrintSummary(output);

                    foreach (var staticComboEntry in shader.StaticComboEntries)
                    {
                        staticComboEntry.Value.Unserialize();
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

                if (OutputFile != null && GltfExportFormat != null)
                {
                    var outFilePath = Path.ChangeExtension(GetOutputPath(path), GltfExportFormat);
                    Directory.CreateDirectory(Path.GetDirectoryName(outFilePath)!);

                    using var fileLoader = CreateGameFileLoader(null, path);
                    CreateGltfExporter(fileLoader).Export(navMeshFile, path, outFilePath);
                    return;
                }

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
            var kv3 = new BinaryKV3()
            {
                Resource = null!
            };

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
            // When processing the files inside of the package, they are counted and print their own header instead
            var processVpkFiles = OutputFile == null && !VerifyVPKChecksums && !ListResources && (CollectStats || ShouldPrintBlockContents || HasShaderOptions);

            if (!processVpkFiles && !ListResources)
            {
                PrintFileHeader(path, null);
            }

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

            Debug.Assert(package.Entries != null);

            if (OutputFile == null)
            {
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

                        return SupportedFileNamesRegex().IsMatch($".{x.Key}");
                    }).ToList();
                }

                if (ListResources)
                {
                    var listEntries = orderedEntries.SelectMany(x => x.Value).ToList();
                    listEntries.Sort((a, b) => string.CompareOrdinal(a.GetFullPath(), b.GetFullPath()));

                    // When listing multiple packages, prefix each entry with the package it came from
                    var packagePrefix = IsInputFolder ? $"{GetDisplayPath(path)} -> " : string.Empty;

                    foreach (var (entry, _) in FilteredEntries(listEntries))
                    {
                        Console.WriteLine(OutputVPKDir
                            ? $"{packagePrefix}{entry}"
                            : $"{packagePrefix}{entry.GetFullPath()} CRC:{entry.CRC32:x10} size:{entry.TotalLength}");
                    }

                    return;
                }

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

                    // The package itself was counted as one of the files to process
                    Interlocked.Add(ref TotalFiles, queue.Count - 1);

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
                        using var fileLoader = StatsWithLoader ? CreateGameFileLoader(package, package.FileName) : null;

                        while (queue.TryDequeue(out var file))
                        {
                            package.ReadEntry(file, out var output);

                            using var entryStream = new MemoryStream(output);
                            ProcessFile(file.GetFullPath(), entryStream, path, fileLoader);
                        }
                    }
                }
                else
                {
                    Console.WriteLine("--- Files in package:");

                    foreach (var entry in orderedEntries)
                    {
                        var count = HasPathFilter ? FilteredEntries(entry.Value).Count() : entry.Value.Count;

                        if (count > 0)
                        {
                            AnyFileMatched = true;
                            Console.WriteLine($"\t{entry.Key}: {count} files");
                        }
                    }
                }
            }
            else
            {
                if (!OutputIsDirectory && MatchesMultipleFiles(package))
                {
                    ReportError("Output path has a file extension, but more than one file matched. Use --vpk_filepath to match exactly one file, or end the output path with a path separator to write into a folder with that name.");
                    return;
                }

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

                using var fileLoader = CreateGameFileLoader(package, package.FileName);

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
            if (!MatchesExtensionFilter(type))
            {
                return;
            }

            Debug.Assert(package.Entries != null);

            if (!package.Entries.TryGetValue(type, out var entries))
            {
                Console.WriteLine("There are no files of type \"{0}\".", type);

                return;
            }

            var gltfExporter = CreateGltfExporter(fileLoader);
            var shaderFilesToDecompile = type == "vcs" ? GetShaderFilesToDecompile(entries) : null;

            foreach (var (file, filePath) in FilteredEntries(entries))
            {
                var extension = type;

                if (OutputFile != null && CachedManifest)
                {
                    if (manifestData.TryGetValue(filePath, out var oldCrc32) && oldCrc32 == file.CRC32)
                    {
                        Console.WriteLine("--- Skipped (unchanged) \"{0}\"", filePath);
                        continue;
                    }

                    manifestData[filePath] = file.CRC32;
                }

                if (OutputFile == null)
                {
                    Console.WriteLine("\t[archive index: {0:D3}] {1}", file.ArchiveIndex, filePath);
                }

                var totalLength = (int)file.TotalLength;
                var rawFileData = ArrayPool<byte>.Shared.Rent(totalLength);
                ContentFile? contentFile = null;

                // Must outlive DumpContentFile because content subfiles can be generated lazily from the resource.
                Resource? resource = null;

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

                            outputFile = GetOutputPath(outputFile);

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
                            // The whole shader is decompiled once, from the highest shader model that was matched
                            if (!shaderFilesToDecompile!.Contains(filePath))
                            {
                                continue;
                            }

                            // Remove the last part from the file name ("_features", ...)
                            var fileName = Path.GetFileNameWithoutExtension(filePath);
                            var newFileNameBase = fileName.AsSpan(0, fileName.LastIndexOf('_'));
                            var directory = Path.GetDirectoryName(filePath);

                            outputFile = Path.Combine(directory ?? string.Empty, string.Concat(newFileNameBase, ".vfx"));

                            var collection = ShaderCollection.GetShaderCollection(filePath, package);
                            contentFile = new ShaderExtract(collection).ToContentFile();
                        }
                        else
                        {
#pragma warning disable CA2000 // False positive, resource is disposed in the finally block
                            resource = new Resource
                            {
                                FileName = filePath,
                            };
#pragma warning restore CA2000
                            resource.Read(memory);

                            if (GltfExportFormat != null && GltfModelExporter.CanExport(resource))
                            {
                                outputFile = filePath;

                                if (RecursiveSearchArchives)
                                {
                                    outputFile = Path.Combine(parentPath, outputFile);
                                }

                                outputFile = GetOutputPath(outputFile);
                                outputFile = Path.ChangeExtension(outputFile, GltfExportFormat);

                                Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);

                                gltfExporter.Export(resource, outputFile);
                                Interlocked.Increment(ref WrittenFiles);

                                Console.WriteLine("--- Dump written to \"{0}\"", outputFile);

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

                        outputFile = GetOutputPath(outputFile);

                        DumpContentFile(outputFile, contentFile, singleFileOutput: !OutputIsDirectory);
                    }
                }
                catch (Exception e)
                {
                    LogException(e, filePath, parentPath);
                }
                finally
                {
                    contentFile?.Dispose();
                    resource?.Dispose();
                    ArrayPool<byte>.Shared.Return(rawFileData);
                }
            }
        }

        private GameFileLoader CreateGameFileLoader(Package? package, string? path)
        {
            var fileLoader = new GameFileLoader(package, path, FileLoaderLogger);

            if (GamePath != null)
            {
                fileLoader.FindAndLoadSearchPaths(GamePath);
            }

            return fileLoader;
        }

        private GltfModelExporter CreateGltfExporter(IFileLoader fileLoader)
        {
            var gltfModelExporter = new GltfModelExporter(fileLoader)
            {
                ExportAnimations = GltfExportAnimations,
                ExportMaterials = GltfExportMaterials,
                AdaptTextures = GltfExportAdaptTextures,
                ExportExtras = GltfExportExtras,
                ComposeAdditiveAnimations = GltfComposeAdditive,
                ProgressReporter = ProgressReporter,
            };

            gltfModelExporter.AnimationFilter.UnionWith(GltfAnimationFilter);
            gltfModelExporter.MeshFilter.UnionWith(GltfMeshFilter);

            return gltfModelExporter;
        }

        /// <summary>
        /// Picks one file per shader to decompile it from, out of the highest shader model among the matched files.
        /// </summary>
        private HashSet<string> GetShaderFilesToDecompile(IEnumerable<PackageEntry> entries)
        {
            var highestByShader = new Dictionary<(string ShaderName, VcsPlatformType Platform), (string FilePath, VcsShaderModelType ShaderModel)>();

            foreach (var (entry, filePath) in FilteredEntries(entries))
            {
                if (entry.TypeName != "vcs")
                {
                    continue;
                }

                var (shaderName, _, platformType, shaderModel) = ShaderUtilHelpers.ComputeVCSFileName(filePath);

                if (!highestByShader.TryGetValue((shaderName, platformType), out var existing) || shaderModel > existing.ShaderModel)
                {
                    highestByShader[(shaderName, platformType)] = (filePath, shaderModel);
                }
            }

            return highestByShader.Values.Select(x => x.FilePath).ToHashSet();
        }

        private void DumpContentFile(string path, ContentFile contentFile, bool dumpSubFiles = true, bool singleFileOutput = false)
        {
            if (OutputToConsole)
            {
                PrintContentFile(path, contentFile);
                return;
            }

            if (contentFile.Data != null)
            {
                DumpFile(path, contentFile.Data);
            }

            foreach (var additionalFile in contentFile.AdditionalFiles)
            {
                // Additional files (animation-graph clips) carry their full resource path. With a real output
                // directory we keep it; otherwise we flatten to the leaf name next to the parent file, which can
                // collide on shared names. Resolving these relative to the parent's output path properly needs a
                // bigger rework of the extract path handling (also in the GUI's PackageExporter).
                var additionalPath = additionalFile.KeepFullPath && OutputIsDirectory
                    ? Path.Combine(OutputFile!, additionalFile.FileName)
                    : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(additionalFile.FileName));
                DumpContentFile(additionalPath, additionalFile);
            }

            if (dumpSubFiles)
            {
                if (singleFileOutput && contentFile.Data == null && contentFile.SubFiles.Count == 1)
                {
                    var data = GetMainFileData(contentFile);
                    if (data != null)
                    {
                        DumpFile(path, data);
                    }

                    return;
                }

                foreach (var contentSubFile in contentFile.SubFiles)
                {
                    var data = contentSubFile.Extract?.Invoke();
                    if (data != null)
                    {
                        DumpFile(Path.Combine(Path.GetDirectoryName(path)!, contentSubFile.FileName), data);
                    }
                }
            }
        }

        /// <summary>
        /// Prints the main file of a decompiled resource, the files it would write alongside are skipped.
        /// </summary>
        private void PrintContentFile(string path, ContentFile contentFile)
        {
            var skippedFiles = contentFile.AdditionalFiles.Count + contentFile.SubFiles.Count;

            if (contentFile.Data == null && contentFile.SubFiles.Count == 1)
            {
                skippedFiles--;
            }

            var data = GetMainFileData(contentFile);

            if (data != null)
            {
                DumpFile(path, data);
            }

            if (skippedFiles > 0)
            {
                Console.Error.WriteLine($"--- Not printing {skippedFiles} additional files of \"{GetConsoleOutputPath(path)}\", use --output <folder> to write them");
            }
        }

        /// <summary>
        /// Gets the data of the file a resource decompiles to, which is a sub file for some types such as textures.
        /// </summary>
        private static byte[]? GetMainFileData(ContentFile contentFile)
        {
            if (contentFile.Data == null && contentFile.SubFiles.Count == 1)
            {
                return contentFile.SubFiles[0].Extract?.Invoke();
            }

            return contentFile.Data;
        }

        private string GetConsoleOutputPath(string path) => Path.GetRelativePath(OutputFile!, path).Replace('\\', '/');

        private void DumpFile(string path, ReadOnlySpan<byte> data)
        {
            if (OutputToConsole)
            {
                lock (ConsoleWriterLock)
                {
                    Stdout.WriteLine($"--- {GetConsoleOutputPath(path)}");
                    Stdout.Flush();

                    using var stdout = Console.OpenStandardOutput();
                    stdout.Write(data);
                    stdout.Write("\n"u8);
                }

                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllBytes(path, data.ToArray());
            Interlocked.Increment(ref WrittenFiles);

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

                AnyFileMatched = true;
                yield return (entry, filePath);
            }
        }

        private bool IsExcludedVpkFilePath(string filePath)
        {
            if (LinkedFilePath != null)
            {
                return !filePath.Equals(LinkedFilePath, StringComparison.OrdinalIgnoreCase);
            }

            return FileFilter.Length > 0 && FileFilter.All(filter => !IsVpkFilePathMatch(filter, filePath));
        }

        private static bool IsVpkFilePathMatch(string filter, string filePath)
        {
            // Filters with wildcards match the full path, otherwise they match the start of the path
            if (filter.AsSpan().ContainsAny('*', '?'))
            {
                // Backslash is an escape character in simple expressions
                return FileSystemName.MatchesSimpleExpression(filter.Replace('\\', '/'), filePath.Replace('\\', '/'), ignoreCase: true);
            }

            return filePath.StartsWith(filter, StringComparison.OrdinalIgnoreCase);
        }

        private string GetDisplayPath(string path)
        {
            return IsInputFolder && path.StartsWith(InputFile, StringComparison.Ordinal) ? path[InputFile.Length..] : path;
        }

        private bool MatchesExtensionFilter(string type) => ExtFilterList == null || ExtFilterList.Contains(type);

        /// <summary>
        /// Whether more than one file would be written from the package, shaders are written once from all of their files.
        /// </summary>
        private bool MatchesMultipleFiles(Package package)
        {
            Debug.Assert(package.Entries != null);

            var count = 0;

            foreach (var (type, entries) in package.Entries)
            {
                if (!MatchesExtensionFilter(type))
                {
                    continue;
                }

                count += Decompile && type == "vcs"
                    ? GetShaderFilesToDecompile(entries).Count
                    : FilteredEntries(entries).Take(2).Count();

                if (count > 1)
                {
                    return true;
                }
            }

            return false;
        }

        private string GetOutputPath(string inputPath)
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
            else if (OutputIsDirectory)
            {
                if (Path.IsPathRooted(inputPath))
                {
                    inputPath = Path.GetFileName(inputPath);
                }

                return Path.Combine(OutputFile, inputPath);
            }

            return Path.GetFullPath(OutputFile);
        }

        /// <summary>
        /// This method tries to run through all the code paths for a particular resource,
        /// which allows us to quickly find exceptions when running --test over an entire game folder.
        /// </summary>
        private void TestAndCollectStats(Resource resource, string path, string? originalPath, IFileLoader? fileLoader = null)
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
                        info = ctrlData.Data.Root.GetStringProperty("_class");
                    }

                    break;

                case ResourceType.EntityLump:
                    // TODO: Also collect unknown attribute/material param hashes from compiled shader dynamic expressions
                    if (DumpUnknownEntityKeys)
                    {
                        var entityLump = (EntityLump?)resource.DataBlock;
                        Debug.Assert(entityLump != null);
                        var entities = entityLump.GetEntities();
                        knownEntityKeys ??= [.. EntityLumpKnownKeys.KnownKeys];

                        foreach (var entity in entities)
                        {
                            foreach (var property in entity.Children)
                            {
                                if (!knownEntityKeys.Contains(property.Key) && property.Key.StartsWith("vrf_unknown_key_", StringComparison.Ordinal))
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
                            AddStatLocal($"Initializer: {op.GetStringProperty("_class")}");
                        }

                        foreach (var op in particleSystem.GetRenderers())
                        {
                            AddStatLocal($"Renderer: {op.GetStringProperty("_class")}");
                        }

                        foreach (var op in particleSystem.GetEmitters())
                        {
                            AddStatLocal($"Emitter: {op.GetStringProperty("_class")}");
                        }

                        foreach (var op in particleSystem.GetOperators())
                        {
                            AddStatLocal($"Operator: {op.GetStringProperty("_class")}");
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

            InternalTestExtraction.Test(resource, fileLoader);

            if (GltfTest && GltfModelExporter.CanExport(resource) && resource.ResourceType != ResourceType.Map)
            {
                var gltfModelExporter = new GltfModelExporter(fileLoader ?? new NullFileLoader())
                {
                    ExportMaterials = false,
                    ExportExtras = GltfExportExtras,
                };
                gltfModelExporter.Export(resource, null); // Filename passed as null which tells exporter to write gltf to a null stream
            }
        }

        private void LogException(Exception e, string path, string? parentPath = null)
        {
            var exceptionsFileName = CollectStats ? $"exceptions{Path.GetExtension(path)}.txt" : "exceptions.txt";

            lock (ConsoleWriterLock)
            {
                FailedFiles++;
                LoggedExceptions = true;

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

        private void ReportError(string message)
        {
            lock (ConsoleWriterLock)
            {
                FailedFiles++;
                Console.Error.WriteLine(message);
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
            info.AppendLine("Website: https://s2v.app");
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
