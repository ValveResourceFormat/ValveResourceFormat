using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

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
        private readonly Dictionary<string, uint> uniqueParticleClasses = new Dictionary<string, uint>();

        private readonly object ConsoleWriterLock = new object();
        private int CurrentFile = 0;
        private int TotalFiles = 0;

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

        private string[] ExtFilterList;

        // This decompiler is a test bed for our library,
        // don't expect to see any quality code in here
        public static int Main(string[] args) => CommandLineApplication.Execute<Decompiler>(args);

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

            var paths = new List<string>();

            if (Directory.Exists(InputFile))
            {
                if (OutputFile != null && File.Exists(OutputFile))
                {
                    Console.Error.WriteLine("Output path is an existing file, but input is a folder.");

                    return 1;
                }

                paths.AddRange(Directory.EnumerateFiles(InputFile, "*.*", RecursiveSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Where(s => s.EndsWith("_c") || s.EndsWith(".vcs")));
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

            if (paths.Count == 0)
            {
                Console.Error.WriteLine("No such file \"{0}\" or directory is empty. Did you mean to include --recursive parameter?", InputFile);

                return 1;
            }

            CurrentFile = 0;
            TotalFiles = paths.Count;

            if (MaxParallelismThreads > 1)
            {
                Console.WriteLine("Will use {0} threads concurrently.", MaxParallelismThreads);

                Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelismThreads }, (path, state) =>
                {
                    ProcessFile(path);
                });
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
                    var info = stat.Value.Info != string.Empty ? string.Format(" ({0})", stat.Value.Info) : string.Empty;

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

                Console.WriteLine();
                Console.WriteLine("Particle classes:");

                foreach (var stat in uniqueParticleClasses.OrderByDescending(x => x.Value))
                {
                    Console.WriteLine("{0} in {1}", stat.Key, stat.Value);
                }
            }

            return 0;
        }

        private void ProcessFile(string path)
        {
            var extension = Path.GetExtension(path);

            if (extension == ".vpk")
            {
                ParseVPK(path);

                return;
            }

            if (extension == ".vcs")
            {
                ParseVCS(path);

                return;
            }

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

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                ProcessFile(path, stream);
            }
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
                            var texture = (Texture)resource.Blocks[BlockType.DATA];
                            info = texture.Format.ToString();
                            texture.GenerateBitmap();
                            break;

                        case ResourceType.Sound:
                            info = ((Sound)resource.Blocks[BlockType.DATA]).Type.ToString();
                            break;

                        case ResourceType.Particle:
                            var particle = new ParticleSystem(resource);
                            var particleClasses =
                                particle.GetEmitters()
                                .Concat(particle.GetRenderers())
                                .Concat(particle.GetOperators())
                                .Concat(particle.GetInitializers())
                                .Select(r => r.GetProperty<string>("_class"));

                            foreach (var particleClass in particleClasses)
                            {
                                uniqueParticleClasses.TryGetValue(particleClass, out var currentCount);
                                uniqueParticleClasses[particleClass] = currentCount + 1;
                            }

                            break;
                    }

                    if (info != string.Empty)
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
                        block.Value.ToString();
                    }
                }

                if (OutputFile != null)
                {
                    var data = FileExtract.Extract(resource);

                    var filePath = Path.ChangeExtension(path, extension);

                    if (RecursiveSearch)
                    {
                        // I bet this is prone to breaking, is there a better way?
                        filePath = filePath.Remove(0, InputFile.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                    }
                    else
                    {
                        filePath = Path.GetFileName(filePath);
                    }

                    DumpFile(filePath, data, !RecursiveSearch);
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

            if (resource.Blocks.ContainsKey(BlockType.RERL))
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

            if (false)
            {
                // TODO: Resource Deferred Refs:
            }
            else
            {
                Console.WriteLine("--- (No Deferred Resource References Found)");
            }

            Console.WriteLine(Environment.NewLine);

            Console.WriteLine("--- Resource Blocks: Count {0} ---", resource.Blocks.Count);

            foreach (var block in resource.Blocks)
            {
                Console.WriteLine("\t-- Block: {0,-4}  Size: {1,-6} bytes [Offset: {2,6}]", block.Key, block.Value.Size, block.Value.Offset);
            }

            if (PrintAllBlocks || !string.IsNullOrEmpty(BlockToPrint))
            {
                Console.WriteLine(Environment.NewLine);

                foreach (var block in resource.Blocks)
                {
                    if (!PrintAllBlocks && BlockToPrint != block.Key.ToString())
                    {
                        continue;
                    }

                    Console.WriteLine("--- Data for block \"{0}\" ---", block.Key);
                    Console.WriteLine(block.Value);
                }
            }
        }

        private void ParseVCS(string path)
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
                shader.Read(path);
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

        private void ParseVPK(string path)
        {
            lock (ConsoleWriterLock)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("--- Listing files in package \"{0}\" ---", path);
                Console.ResetColor();
            }

            var package = new Package();

            try
            {
                package.Read(path);
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

                var orderedEntries = package.Entries.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key);

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

                            using (var stream = new MemoryStream(output))
                            {
                                ProcessFile(file.GetFullPath(), stream);
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
                    using (var resource = new Resource())
                    {
                        using (var memory = new MemoryStream(output))
                        {
                            try
                            {
                                resource.Read(memory);

                                extension = FileExtract.GetExtension(resource);

                                if (extension == null)
                                {
                                    extension = type.Substring(0, type.Length - 2);
                                }

                                output = FileExtract.Extract(resource).ToArray();
                            }
                            catch (Exception e)
                            {
                                lock (ConsoleWriterLock)
                                {
                                    Console.ForegroundColor = ConsoleColor.DarkRed;
                                    Console.WriteLine("\t" + e.Message + " on resource type " + type + ", extracting as-is");
                                    Console.ResetColor();
                                }
                            }
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

        private string FixPathSlashes(string path)
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
