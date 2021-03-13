using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ToolsAssetInfo;

namespace Decompiler
{
    [Command(Name = "vrf_decompiler", Description = "A test bed command line interface for the VRF library")]
    [VersionOptionFromMember(MemberName = nameof(GetVersion))]
    public class Decompiler
    {
        private static string GetVersion() => typeof(Decompiler).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

        private readonly Dictionary<string, uint> OldPakManifest = new Dictionary<string, uint>();
        private readonly Dictionary<string, ResourceStat> stats = new Dictionary<string, ResourceStat>();
        private readonly Dictionary<string, string> uniqueSpecialDependancies = new Dictionary<string, string>();

        private readonly object ConsoleWriterLock = new object();
        private int CurrentFile;
        private int TotalFiles;

        [Required]
        [Option("-i|--input", "Input file to be processed. With no additional arguments, a summary of the input(s) will be displayed.", CommandOptionType.SingleValue)]
        public string InputFile { get; private set; }

        [Option("--recursive", "If specified and given input is a folder, all sub directories will be scanned too.", CommandOptionType.NoValue)]
        public bool RecursiveSearch { get; private set; }

        [Option("-o|--output", "Writes DATA output to file.", CommandOptionType.SingleValue)]
        public string OutputFile { get; private set; }

        [Option("-a|--all", "Prints the content of each resource block in the file.", CommandOptionType.NoValue)]
        public bool PrintAllBlocks { get; }

        [Option("-b|--block", "Print the content of a specific block. Specify the block via its 4CC name - case matters! (eg. DATA, RERL, REDI, NTRO).", CommandOptionType.SingleValue)]
        public string BlockToPrint { get; }

        [Option("--stats", "Collect stats on all input files and then print them.", CommandOptionType.NoValue)]
        public bool CollectStats { get; }

        [Option("--threads", "If more than 1, files will be processed concurrently.", CommandOptionType.SingleValue)]
        public int MaxParallelismThreads { get; } = 1;

        [Option("--vpk_dir", "Write a file with files in given VPK and their CRC.", CommandOptionType.NoValue)]
        public bool OutputVPKDir { get; }

        [Option("--vpk_verify", "Verify checksums and signatures.", CommandOptionType.NoValue)]
        public bool VerifyVPKChecksums { get; }

        [Option("--vpk_cache", "Use cached VPK manifest to keep track of updates. Only changed files will be written to disk.", CommandOptionType.NoValue)]
        public bool CachedManifest { get; }

        [Option("-d|--vpk_decompile", "Decompile supported files", CommandOptionType.NoValue)]
        public bool Decompile { get; }

        [Option("-e|--vpk_extensions", "File extension(s) filter, example: \"vcss_c,vjs_c,vxml_c\"", CommandOptionType.SingleValue)]
        public string ExtFilter { get; }

        [Option("-f|--vpk_filepath", "File path filter, example: panorama\\ or \"panorama\\\\\"", CommandOptionType.SingleValue)]
        public string FileFilter { get; private set; }

        [Option("-l|--vpk_list", "Lists all resources in given VPK. File extension and path filters apply.", CommandOptionType.NoValue)]
        public bool ListResources { get; }

        [Option("--gltf_export_format", "Exports meshes/models in given glTF format. Must be either 'gltf' (default) or 'glb'", CommandOptionType.SingleValue)]
        public string GltfExportFormat { get; } = "gltf";

        [Option("--gltf_export_materials", "Whether to export materials during glTF exports (warning: slow!)", CommandOptionType.NoValue)]
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

                IsInputFolder = true;

                var dirs = Directory
                    .EnumerateFiles(InputFile, "*.*", RecursiveSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(s => s.EndsWith("_c") || s.EndsWith(".vcs"));

                if (!dirs.Any())
                {
                    Console.Error.WriteLine(
                        "Unable to find any \"_c\" compiled files in \"{0}\" folder.{1}",
                        InputFile,
                        RecursiveSearch ? " Did you mean to include --recursive parameter?" : string.Empty);

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

            var magicData = new byte[4];
            fs.Read(magicData, 0, magicData.Length);
            fs.Position = 0;

            var magic = BitConverter.ToUInt32(magicData, 0);

            switch (magic)
            {
                case Package.MAGIC: ParseVPK(path, fs); return;
                case CompiledShader.MAGIC: ParseVCS(path, fs); return;
                case ToolsAssetInfo.MAGIC2:
                case ToolsAssetInfo.MAGIC: ParseToolsAssetInfo(path, fs); return;
                case BinaryKV3.MAGIC3:
                case BinaryKV3.MAGIC2:
                case BinaryKV3.MAGIC: ParseKV3(fs); return;
            }

            var extension = Path.GetExtension(path);

            if (extension == ".vfont")
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

            ProcessFile(path, fs);
        }

        private void ProcessFile(string path, Stream stream)
        {
            var resource = new Resource();

            try
            {
                resource.Read(stream);

                var extension = FileExtract.GetExtension(resource);

                if (extension == null)
                {
                    extension = Path.GetExtension(path);

                    if (extension.EndsWith("_c", StringComparison.Ordinal))
                    {
                        extension = extension.Substring(0, extension.Length - 2);
                    }
                }

                if (CollectStats)
                {
                    string id = string.Format("{0}_{1}", resource.ResourceType, resource.Version);
                    string info = string.Empty;

                    switch (resource.ResourceType)
                    {
                        case ResourceType.Texture:
                            var texture = (Texture)resource.DataBlock;
                            info = texture.Format.ToString();
                            texture.GenerateBitmap();
                            break;

                        case ResourceType.Sound:
                            info = ((Sound)resource.DataBlock).SoundType.ToString();
                            break;
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
                                uniqueSpecialDependancies[string.Format("{0} \"{1}\"", dep.CompilerIdentifier, dep.String)] = path;
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
                    var data = FileExtract.Extract(resource);

                    var filePath = Path.ChangeExtension(path, extension);

                    if (IsInputFolder)
                    {
                        // I bet this is prone to breaking, is there a better way?
                        filePath = filePath.Remove(0, InputFile.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                    }
                    else
                    {
                        filePath = Path.GetFileName(filePath);
                    }

                    DumpFile(filePath, data, !IsInputFolder);
                }
            }
            catch (Exception e)
            {
                File.AppendAllText("exceptions.txt", string.Format("---------------\nFile: {0}\nException: {1}\n\n", path, e));

                lock (ConsoleWriterLock)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }
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
                    var fileName = Path.GetFileName(path);
                    fileName = Path.ChangeExtension(fileName, "txt");

                    DumpFile(fileName, assetsInfo.ToString(), true);
                }
                else
                {
                    Console.WriteLine(assetsInfo.ToString());
                }
            }
            catch (Exception e)
            {
                lock (ConsoleWriterLock)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }
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

            var shader = new CompiledShader();

            try
            {
                shader.Read(path, stream);
            }
            catch (Exception e)
            {
                lock (ConsoleWriterLock)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }
            }

            shader.Dispose();
        }

        private void ParseVFont(string path)
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
                    var fileName = Path.GetFileName(path);
                    fileName = Path.ChangeExtension(fileName, "ttf");

                    DumpFile(fileName, output, true);
                }
            }
            catch (Exception e)
            {
                lock (ConsoleWriterLock)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }
            }
        }

        private void ParseKV3(Stream stream)
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
                lock (ConsoleWriterLock)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }
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
                lock (ConsoleWriterLock)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }
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
                    lock (ConsoleWriterLock)
                    {
                        Console.WriteLine("Failed to verify checksums and signature of given VPK:");
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(e.Message);
                        Console.ResetColor();
                    }
                }

                return;
            }

            if (OutputFile == null)
            {
                Console.WriteLine("--- Files in package:");

                var orderedEntries = package.Entries.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key).ToList();

                if (ExtFilterList != null)
                {
                    orderedEntries = orderedEntries.Where(x => ExtFilterList.Contains(x.Key)).ToList();
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
                    TotalFiles += orderedEntries
                        .Where(entry => entry.Key.EndsWith("_c", StringComparison.Ordinal))
                        .Sum(x => x.Value.Count);
                }

                foreach (var entry in orderedEntries)
                {
                    Console.WriteLine("\t{0}: {1} files", entry.Key, entry.Value.Count);

                    if (CollectStats && entry.Key.EndsWith("_c", StringComparison.Ordinal))
                    {
                        foreach (var file in entry.Value)
                        {
                            lock (ConsoleWriterLock)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("[{0}/{1}] {2}", ++CurrentFile, TotalFiles, file.GetFullPath());
                                Console.ResetColor();
                            }

                            package.ReadEntry(file, out var output);

                            using (var entryStream = new MemoryStream(output))
                            {
                                ProcessFile(file.GetFullPath(), entryStream);
                            }
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("--- Dumping decompiled files...");

                var manifestPath = string.Concat(path, ".manifest.txt");

                if (CachedManifest && File.Exists(manifestPath))
                {
                    var file = new StreamReader(manifestPath);
                    string line;

                    while ((line = file.ReadLine()) != null)
                    {
                        var split = line.Split(new[] { ' ' }, 2);

                        if (split.Length == 2)
                        {
                            OldPakManifest.Add(split[1], uint.Parse(split[0]));
                        }
                    }

                    file.Close();
                }

                foreach (var type in package.Entries)
                {
                    DumpVPK(package, type.Key);
                }

                if (CachedManifest)
                {
                    using (var file = new StreamWriter(manifestPath))
                    {
                        foreach (var hash in OldPakManifest)
                        {
                            if (package.FindEntry(hash.Key) == null)
                            {
                                Console.WriteLine("\t{0} no longer exists in VPK", hash.Key);
                            }

                            file.WriteLine("{0} {1}", hash.Value, hash.Key);
                        }
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

        private void DumpVPK(Package package, string type)
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

                if (OutputFile != null)
                {
                    if (CachedManifest && OldPakManifest.TryGetValue(filePath, out var oldCrc32) && oldCrc32 == file.CRC32)
                    {
                        continue;
                    }

                    OldPakManifest[filePath] = file.CRC32;
                }

                Console.WriteLine("\t[archive index: {0:D3}] {1}", file.ArchiveIndex, filePath);

                package.ReadEntry(file, out var output);

                if (type.EndsWith("_c", StringComparison.Ordinal) && Decompile)
                {
                    using var resource = new Resource();
                    using var memory = new MemoryStream(output);

                    try
                    {
                        resource.Read(memory);

                        extension = FileExtract.GetExtension(resource);

                        if (extension == null)
                        {
                            extension = type.Substring(0, type.Length - 2);
                        }

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

                        output = FileExtract.Extract(resource).ToArray();
                    }
                    catch (Exception e)
                    {
                        File.AppendAllText("exceptions.txt", $"---------------\nFile: {filePath}\nException: {e}\n\n");

                        lock (ConsoleWriterLock)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkRed;
                            Console.WriteLine("\t" + e.Message + " on resource type " + type + ", extracting as-is");
                            Console.ResetColor();
                        }
                    }
                }

                if (OutputFile != null)
                {
                    if (type != extension)
                    {
                        filePath = Path.ChangeExtension(filePath, extension);
                    }

                    DumpFile(filePath, output);
                }
            }
        }

        private void DumpFile(string path, Span<byte> data, bool useOutputAsFullPath = false)
        {
            var outputFile = useOutputAsFullPath ? Path.GetFullPath(OutputFile) : Path.Combine(OutputFile, path);

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

            File.WriteAllBytes(outputFile, data.ToArray());

            Console.WriteLine("--- Dump written to \"{0}\"", outputFile);
        }

        private void DumpFile(string path, string data, bool useOutputAsFullPath = false)
        {
            var outputFile = useOutputAsFullPath ? Path.GetFullPath(OutputFile) : Path.Combine(OutputFile, path);

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

            File.WriteAllText(outputFile, data);

            Console.WriteLine("--- Dump written to \"{0}\"", outputFile);
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
    }
}
