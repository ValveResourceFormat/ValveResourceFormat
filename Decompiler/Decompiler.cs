using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ToolsAssetInfo;

namespace Decompiler
{
    [Command(Name = "vrf_decompiler", Description = "A test bed command line interface for the VRF library")]
    [VersionOptionFromMember(MemberName = nameof(GetVersion))]
    public class Decompiler
    {
        private readonly Dictionary<string, ResourceStat> stats = new();
        private readonly Dictionary<string, string> uniqueSpecialDependancies = new();

        private readonly object ConsoleWriterLock = new();
        private int CurrentFile;
        private int TotalFiles;

        [Required]
        [Option("-i|--input", "Input file to be processed. With no additional arguments, a summary of the input(s) will be displayed.", CommandOptionType.SingleValue)]
        public string InputFile { get; private set; }

        [Option("-o|--output", "Output path to write to. If input is a folder (or a VPK), this should be a folder.", CommandOptionType.SingleValue)]
        public string OutputFile { get; private set; }

        [Option("--recursive", "If specified and given input is a folder, all sub directories will be scanned too.", CommandOptionType.NoValue)]
        public bool RecursiveSearch { get; private set; }

        [Option("--recursive_vpk", "If specified along with --recursive, will also recurse into VPK archives.", CommandOptionType.NoValue)]
        public bool RecursiveSearchArchives { get; private set; }

        [Option("-a|--all", "Print the content of each resource block in the file.", CommandOptionType.NoValue)]
        public bool PrintAllBlocks { get; }

        [Option("-b|--block", "Print the content of a specific block, example: DATA, RERL, REDI, NTRO.", CommandOptionType.SingleValue)]
        public string BlockToPrint { get; }

        [Option("--stats", "Collect stats on all input files and then print them. (This is testing VRF over all files at once)", CommandOptionType.NoValue)]
        public bool CollectStats { get; }

        [Option("--threads", "If more than 1, files will be processed concurrently.", CommandOptionType.SingleValue)]
        public int MaxParallelismThreads { get; } = 1;

        [Option("--vpk_dir", "Write a file with files in given VPK and their CRC.", CommandOptionType.NoValue)]
        public bool OutputVPKDir { get; }

        [Option("--vpk_verify", "Verify checksums and signatures.", CommandOptionType.NoValue)]
        public bool VerifyVPKChecksums { get; }

        [Option("--vpk_cache", "Use cached VPK manifest to keep track of updates. Only changed files will be written to disk.", CommandOptionType.NoValue)]
        public bool CachedManifest { get; }

        [Option("-d|--vpk_decompile", "Decompile supported resource files.", CommandOptionType.NoValue)]
        public bool Decompile { get; }

        [Option("-e|--vpk_extensions", "File extension(s) filter, example: \"vcss_c,vjs_c,vxml_c\".", CommandOptionType.SingleValue)]
        public string ExtFilter { get; }

        [Option("-f|--vpk_filepath", "File path filter, example: \"panorama\\\\\" or \"scripts/items/items_game.txt\".", CommandOptionType.SingleValue)]
        public string FileFilter { get; private set; }

        [Option("-l|--vpk_list", "Lists all resources in given VPK. File extension and path filters apply.", CommandOptionType.NoValue)]
        public bool ListResources { get; }

        [Option("--gltf_export_format", "Exports meshes/models in given glTF format. Must be either 'gltf' (default) or 'glb'.", CommandOptionType.SingleValue)]
        public string GltfExportFormat { get; } = "gltf";

        [Option("--gltf_export_materials", "Whether to export materials during glTF exports.", CommandOptionType.NoValue)]
        public bool GltfExportMaterials { get; }

        private string[] ExtFilterList;
        private bool IsInputFolder;

        // This decompiler is a test bed for our library,
        // don't expect to see any quality code in here
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            return CommandLineApplication.Execute<Decompiler>(args);
        }

        private int OnExecute()
        {
            InputFile = Path.GetFullPath(InputFile);

            if (OutputFile != null)
            {
                OutputFile = Path.GetFullPath(OutputFile);
                OutputFile = FixPathSlashes(OutputFile);
            }

            if (FileFilter != null)
            {
                FileFilter = FixPathSlashes(FileFilter);
            }

            if (ExtFilter != null)
            {
                ExtFilterList = ExtFilter.Split(',');
            }

            if (GltfExportFormat != "gltf" && GltfExportFormat != "glb")
            {
                Console.Error.WriteLine("glTF export format must be either 'gltf' or 'glb'.");

                return 1;
            }

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

                var dirs = Directory
                    .EnumerateFiles(InputFile, "*.*", RecursiveSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
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

                        return s.EndsWith("_c", StringComparison.Ordinal) || s.EndsWith(".vcs", StringComparison.Ordinal);
                    })
                    .ToList();

                if (RecursiveSearchArchives)
                {
                    if (!RecursiveSearch)
                    {
                        Console.Error.WriteLine("Option --recursive_vpk must be specified with --recursive.");

                        return 1;
                    }

                    var vpkRegex = new Regex(@"_[0-9]{3}\.vpk$");
                    var vpks = Directory
                        .EnumerateFiles(InputFile, "*.vpk", SearchOption.AllDirectories)
                        .Where(s => !vpkRegex.IsMatch(s));

                    dirs.AddRange(vpks);
                }

                if (!dirs.Any())
                {
                    Console.Error.WriteLine($"Unable to find any \"_c\" compiled files in \"{InputFile}\" folder.");

                    if (!RecursiveSearch)
                    {
                        Console.Error.WriteLine("Perhaps you should specify --recursive option to scan the input folder recursively.");
                    }

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
                if (RecursiveSearchArchives)
                {
                    Console.Error.WriteLine("File passed in with --recursive_vpk option, this is not supported.");

                    return 1;
                }

                paths.Add(InputFile);
            }
            else
            {
                Console.Error.WriteLine("Input \"{0}\" is not a file or a folder.", InputFile);

                return 1;
            }

            CurrentFile = 0;
            TotalFiles = paths.Count;

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
                Console.WriteLine("Processed resource stats:");

                foreach (var stat in stats.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key))
                {
                    var info = string.IsNullOrEmpty(stat.Value.Info) ? string.Empty : $" ({stat.Value.Info})";

                    Console.WriteLine($"{stat.Value.Count,5} resources of version {stat.Value.Version} and type {stat.Value.Type}{info}");

                    foreach (var file in stat.Value.FilePaths)
                    {
                        Console.WriteLine($"\t\t{file}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Unique special dependancies:");

                foreach (var stat in uniqueSpecialDependancies)
                {
                    Console.WriteLine("{0} in {1}", stat.Key, stat.Value);
                }
            }

            return 0;
        }

        private void ProcessFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            ProcessFile(path, fs);
        }

        private void ProcessFile(string path, Stream stream, string originalPath = null)
        {
            var magicData = new byte[4];

            int bytesRead;
            var totalRead = 0;
            while ((bytesRead = stream.Read(magicData, totalRead, magicData.Length - totalRead)) != 0)
            {
                totalRead += bytesRead;
            }

            stream.Seek(-totalRead, SeekOrigin.Current);

            var magic = BitConverter.ToUInt32(magicData, 0);

            switch (magic)
            {
                case Package.MAGIC: ParseVPK(path, stream); return;
                case ShaderFile.MAGIC: ParseVCS(path, stream); return;
                case ToolsAssetInfo.MAGIC2:
                case ToolsAssetInfo.MAGIC: ParseToolsAssetInfo(path, stream); return;
                case BinaryKV3.MAGIC3:
                case BinaryKV3.MAGIC2:
                case BinaryKV3.MAGIC: ParseKV3(path, stream); return;
            }

            var pathExtension = Path.GetExtension(path);

            if (pathExtension == ".vfont")
            {
                ParseVFont(path);

                return;
            }

            lock (ConsoleWriterLock)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[{0}/{1}] {2}", ++CurrentFile, TotalFiles, path);
                Console.ResetColor();
            }

            var resource = new Resource
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

                    if (extension.EndsWith("_c", StringComparison.Ordinal))
                    {
                        extension = extension[..^2];
                    }
                }

                if (CollectStats)
                {
                    var id = $"{resource.ResourceType}_{resource.Version}";
                    var info = string.Empty;

                    switch (resource.ResourceType)
                    {
                        case ResourceType.Texture:
                            var texture = (Texture)resource.DataBlock;
                            info = texture.Format.ToString();
                            break;

                        case ResourceType.Sound:
                            info = ((Sound)resource.DataBlock).SoundType.ToString();
                            break;
                    }

                    if (OutputFile == null)
                    {
                        // Test extraction code flow while collecting stats
                        FileExtract.Extract(resource);
                    }

                    if (!string.IsNullOrEmpty(info))
                    {
                        id = string.Concat(id, "_", info);
                    }

                    lock (stats)
                    {
                        if (stats.ContainsKey(id))
                        {
                            if (stats[id].Count++ < 10)
                            {
                                stats[id].FilePaths.Add(path);
                            }
                        }
                        else
                        {
                            stats.Add(id, new ResourceStat(resource, info, path));
                        }
                    }

                    if (resource.EditInfo != null && resource.EditInfo.Structs.ContainsKey(ResourceEditInfo.REDIStruct.SpecialDependencies))
                    {
                        lock (uniqueSpecialDependancies)
                        {
                            foreach (var dep in ((ValveResourceFormat.Blocks.ResourceEditInfoStructs.SpecialDependencies)resource.EditInfo.Structs[ResourceEditInfo.REDIStruct.SpecialDependencies]).List)
                            {
                                uniqueSpecialDependancies[$"{dep.CompilerIdentifier} \"{dep.String}\""] = path;
                            }
                        }
                    }

                    foreach (var block in resource.Blocks)
                    {
                        block.ToString();
                    }
                }

                if (OutputFile != null)
                {
                    var contentFile = FileExtract.Extract(resource);

                    path = Path.ChangeExtension(path, extension);
                    var outFilePath = GetOutputPath(path);

                    var extensionNew = Path.GetExtension(outFilePath);
                    if (extensionNew.Length == 0 || (extensionNew[1..]) != extension)
                    {
                        lock (ConsoleWriterLock)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Extension '.{extension}' might be more suitable than the one provided '{extensionNew}'");
                            Console.ResetColor();
                        }
                    }

                    DumpContentFile(outFilePath, contentFile);
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

            if (resource.ContainsBlockType(BlockType.RERL))
            {
                Console.WriteLine("--- Resource External Refs: ---");
                Console.WriteLine("\t{0,-16}  {1,-48}", "Id:", "Resource Name:");

                foreach (var res in resource.ExternalReferences.ResourceRefInfoList)
                {
                    Console.WriteLine("\t{0:X16}  {1,-48}", res.Id, res.Name);
                }
            }
            else
            {
                Console.WriteLine("--- (No External Resource References Found)");
            }

            Console.WriteLine(Environment.NewLine);

            // TODO: Resource Deferred Refs:
            Console.WriteLine("--- (No Deferred Resource References Found)");

            Console.WriteLine(Environment.NewLine);

            Console.WriteLine("--- Resource Blocks: Count {0} ---", resource.Blocks.Count);

            foreach (var block in resource.Blocks)
            {
                Console.WriteLine("\t-- Block: {0,-4}  Size: {1,-6} bytes [Offset: {2,6}]", block.Type, block.Size, block.Offset);
            }

            if (PrintAllBlocks || !string.IsNullOrEmpty(BlockToPrint))
            {
                Console.WriteLine(Environment.NewLine);

                foreach (var block in resource.Blocks)
                {
                    if (!PrintAllBlocks && BlockToPrint != block.Type.ToString())
                    {
                        continue;
                    }

                    Console.WriteLine("--- Data for block \"{0}\" ---", block.Type);
                    Console.WriteLine(block.ToString());
                }
            }
        }

        private void ParseToolsAssetInfo(string path, Stream stream)
        {
            var assetsInfo = new ToolsAssetInfo();

            try
            {
                assetsInfo.Read(stream);

                if (OutputFile != null)
                {
                    path = Path.ChangeExtension(path, "txt");
                    path = GetOutputPath(path);

                    DumpFile(path, Encoding.UTF8.GetBytes(assetsInfo.ToString()));
                }
                else
                {
                    Console.WriteLine(assetsInfo.ToString());
                }
            }
            catch (Exception e)
            {
                LogException(e, path);
            }
        }

        private void ParseVCS(string path, Stream stream)
        {
            lock (ConsoleWriterLock)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("--- Loading shader file \"{0}\" ---", path);
                Console.ResetColor();
            }

            var shader = new ShaderFile();

            try
            {
                shader.Read(path, stream);

                if (!CollectStats)
                {
                    shader.PrintSummary();
                }
            }
            catch (Exception e)
            {
                LogException(e, path);
            }

            shader.Dispose();
        }

        private void ParseVFont(string path) // TODO: Accept Stream
        {
            lock (ConsoleWriterLock)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("--- Loading font file \"{0}\" ---", path);
                Console.ResetColor();
            }

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

        private void ParseKV3(string path, Stream stream)
        {
            var kv3 = new BinaryKV3();

            try
            {
                using (var binaryReader = new BinaryReader(stream))
                {
                    kv3.Size = (uint)stream.Length;
                    kv3.Read(binaryReader, null);
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
            lock (ConsoleWriterLock)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("--- Listing files in package \"{0}\" ---", path);
                Console.ResetColor();
            }

            var package = new Package();
            package.SetFileName(path);

            try
            {
                package.Read(stream);
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
                    package.VerifyHashes();

                    Console.WriteLine("VPK verification succeeded");
                }
                catch (Exception e)
                {
                    LogException(e, path);
                }

                return;
            }

            if (OutputFile == null)
            {
                if (!CollectStats)
                {
                    Console.WriteLine("--- Files in package:");
                }

                var orderedEntries = package.Entries.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key).ToList();

                if (ExtFilterList != null)
                {
                    orderedEntries = orderedEntries.Where(x => ExtFilterList.Contains(x.Key)).ToList();
                }
                else if (CollectStats)
                {
                    orderedEntries = orderedEntries.Where(x => x.Key.EndsWith("_c", StringComparison.Ordinal)).ToList();
                }

                if (ListResources)
                {
                    var listEntries = orderedEntries.SelectMany(x => x.Value);
                    foreach (var entry in listEntries)
                    {
                        var filePath = FixPathSlashes(entry.GetFullPath());
                        if (FileFilter != null && !filePath.StartsWith(FileFilter, StringComparison.Ordinal))
                        {
                            continue;
                        }
                        Console.WriteLine("\t{0}", filePath);
                    }
                    return;
                }

                if (CollectStats)
                {
                    TotalFiles += orderedEntries.Sum(x => x.Value.Count);

                    if (MaxParallelismThreads > 1)
                    {
                        var queue = new ConcurrentQueue<PackageEntry>();
                        var tasks = new List<Task>();

                        foreach (var entry in orderedEntries)
                        {
                            foreach (var file in entry.Value)
                            {
                                queue.Enqueue(file);
                            }
                        }

                        var lockEntryRead = new object();

                        for (var n = 0; n < MaxParallelismThreads; n++)
                        {
                            tasks.Add(Task.Run(() =>
                            {
                                while (queue.TryDequeue(out var file))
                                {
                                    byte[] output;

                                    lock (lockEntryRead)
                                    {
                                        package.ReadEntry(file, out output);
                                    }

                                    using var entryStream = new MemoryStream(output);
                                    ProcessFile(file.GetFullPath(), entryStream, path);
                                }
                            }));
                        }

                        Task.WhenAll(tasks).GetAwaiter().GetResult();
                    }
                    else
                    {
                        foreach (var entry in orderedEntries)
                        {
                            foreach (var file in entry.Value)
                            {
                                package.ReadEntry(file, out var output);

                                using var entryStream = new MemoryStream(output);
                                ProcessFile(file.GetFullPath(), entryStream, path);
                            }
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

                var manifestPath = string.Concat(path, ".manifest.txt");
                var manifestData = new Dictionary<string, uint>();

                if (CachedManifest && File.Exists(manifestPath))
                {
                    var file = new StreamReader(manifestPath);
                    string line;

                    while ((line = file.ReadLine()) != null)
                    {
                        var split = line.Split(new[] { ' ' }, 2);

                        if (split.Length == 2)
                        {
                            manifestData.Add(split[1], uint.Parse(split[0], CultureInfo.InvariantCulture));
                        }
                    }

                    file.Close();
                }

                foreach (var type in package.Entries)
                {
                    DumpVPK(path, package, type.Key, manifestData);
                }

                if (CachedManifest)
                {
                    using var file = new StreamWriter(manifestPath);

                    foreach (var hash in manifestData)
                    {
                        if (package.FindEntry(hash.Key) == null)
                        {
                            Console.WriteLine("\t{0} no longer exists in VPK", hash.Key);
                        }

                        file.WriteLine("{0} {1}", hash.Value, hash.Key);
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

        private void DumpVPK(string parentPath, Package package, string type, Dictionary<string, uint> manifestData)
        {
            if (ExtFilterList != null && !ExtFilterList.Contains(type))
            {
                return;
            }

            if (!package.Entries.ContainsKey(type))
            {
                Console.WriteLine("There are no files of type \"{0}\".", type);

                return;
            }

            var fileLoader = new BasicVpkFileLoader(package);
            var entries = package.Entries[type];

            foreach (var file in entries)
            {
                var extension = type;
                var filePath = FixPathSlashes(file.GetFullPath());

                if (FileFilter != null && !filePath.StartsWith(FileFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                if (OutputFile != null && CachedManifest)
                {
                    if (manifestData.TryGetValue(filePath, out var oldCrc32) && oldCrc32 == file.CRC32)
                    {
                        continue;
                    }

                    manifestData[filePath] = file.CRC32;
                }

                Console.WriteLine("\t[archive index: {0:D3}] {1}", file.ArchiveIndex, filePath);

                package.ReadEntry(file, out var output);
                var contentFile = default(ContentFile);

                if (type.EndsWith("_c", StringComparison.Ordinal) && Decompile)
                {
                    using var resource = new Resource
                    {
                        FileName = filePath,
                    };
                    using var memory = new MemoryStream(output);

                    try
                    {
                        resource.Read(memory);

                        extension = FileExtract.GetExtension(resource) ?? type[..^2];

                        // TODO: Hook this up in FileExtract
                        if (resource.ResourceType == ResourceType.Mesh || resource.ResourceType == ResourceType.Model)
                        {
                            var outputExtension = GltfExportFormat;
                            var outputFile = Path.Combine(OutputFile, Path.ChangeExtension(filePath, outputExtension));

                            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

                            var exporter = new GltfModelExporter
                            {
                                ExportMaterials = GltfExportMaterials,
                                ProgressReporter = new Progress<string>(progress => Console.WriteLine($"--- {progress}")),
                                FileLoader = fileLoader
                            };

                            if (resource.ResourceType == ResourceType.Mesh)
                            {
                                exporter.ExportToFile(file.GetFileName(), outputFile, new Mesh(resource));
                            }
                            else if (resource.ResourceType == ResourceType.Model)
                            {
                                exporter.ExportToFile(file.GetFileName(), outputFile, (Model)resource.DataBlock);
                            }

                            continue;
                        }

                        contentFile = FileExtract.Extract(resource);
                    }
                    catch (Exception e)
                    {
                        LogException(e, filePath, package.FileName);
                    }
                }

                if (OutputFile != null)
                {
                    if (RecursiveSearchArchives)
                    {
                        filePath = Path.Combine(parentPath, filePath);
                    }

                    if (type != extension)
                    {
                        filePath = Path.ChangeExtension(filePath, extension);
                    }

                    filePath = GetOutputPath(filePath, useOutputAsDirectory: true);

                    if (Decompile)
                    {
                        DumpContentFile(filePath, contentFile);
                    }
                    else
                    {
                        DumpFile(filePath, output);
                    }
                }
            }
        }

        private static void DumpContentFile(string path, ContentFile contentFile, bool dumpSubFiles = true)
        {
            DumpFile(path, contentFile.Data);

            if (dumpSubFiles)
            {
                foreach (var contentSubFile in contentFile.SubFiles)
                {
                    DumpFile(Path.Combine(Path.GetDirectoryName(path), contentSubFile.FileName), contentSubFile.Extract());
                }
            }
        }

        private static void DumpFile(string path, ReadOnlySpan<byte> data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            File.WriteAllBytes(path, data.ToArray());

            Console.WriteLine("--- Dump written to \"{0}\"", path);
        }

        private string GetOutputPath(string inputPath, bool useOutputAsDirectory = false)
        {
            if (IsInputFolder)
            {
                if (!inputPath.StartsWith(InputFile, StringComparison.Ordinal))
                {
                    throw new Exception($"Path '{inputPath}' does not start with '{InputFile}', is this a bug?");
                }

                inputPath = inputPath.Remove(0, InputFile.Length);

                return Path.Combine(OutputFile, inputPath);
            }
            else if (useOutputAsDirectory)
            {
                return Path.Combine(OutputFile, inputPath);
            }

            return Path.GetFullPath(OutputFile);
        }

        private void LogException(Exception e, string path, string parentPath = null)
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
            info.Append("VRF Version: ");
            info.AppendLine(typeof(Decompiler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
            info.Append("Runtime: ");
            info.AppendLine(RuntimeInformation.FrameworkDescription);
            info.Append("OS: ");
            info.AppendLine(RuntimeInformation.OSDescription);
            info.AppendLine("Website: https://vrf.steamdb.info");
            info.Append("GitHub: https://github.com/SteamDatabase/ValveResourceFormat");
            return info.ToString();
        }
    }
}
