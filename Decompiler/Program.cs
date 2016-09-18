using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Blocks;
using System.Text;
using SteamDatabase.ValvePak;

namespace Decompiler
{
    class Decompiler
    {
        private static readonly object ConsoleWriterLock = new object();
        private static Options Options;
        private static int CurrentFile = 0;
        private static int TotalFiles = 0;

        private static Dictionary<string, ResourceStat> stats = new Dictionary<string, ResourceStat>();
        private static Dictionary<string, string> uniqueSpecialDependancies = new Dictionary<string, string>();

        // This decompiler is a test bed for our library,
        // don't expect to see any quality code in here
        public static void Main(string[] args)
        {
            Options = new Options();
            CommandLine.Parser.Default.ParseArgumentsStrict(args, Options);

            Options.InputFile = Path.GetFullPath(Options.InputFile);

            if (Options.OutputFile != null)
            {
                Options.OutputFile = Path.GetFullPath(Options.OutputFile);
                Options.OutputFile = FixPathSlahes(Options.OutputFile);
            }

            var paths = new List<string>();

            if (Directory.Exists(Options.InputFile))
            {
                if (Options.OutputFile != null && File.Exists(Options.OutputFile))
                {
                    Console.Error.WriteLine("Output path is an existing file, but input is a folder.");

                    return;
                }

                paths.AddRange(Directory.GetFiles(Options.InputFile, "*.*_c", Options.RecursiveSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            }
            else if (File.Exists(Options.InputFile))
            {
                Options.RecursiveSearch = false;

                paths.Add(Options.InputFile);
            }

            if (paths.Count == 0)
            {
                Console.Error.WriteLine("No such file \"{0}\" or directory is empty. Did you mean to include --recursive parameter?", Options.InputFile);

                return;
            }

            CurrentFile = 0;
            TotalFiles = paths.Count;

            if (Options.MaxParallelismThreads > 1)
            {
                Console.WriteLine("Will use {0} threads concurrently.", Options.MaxParallelismThreads);

                Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = Options.MaxParallelismThreads }, (path, state) =>
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

            if (Options.CollectStats)
            {
                Console.WriteLine();
                Console.WriteLine("Processed resource stats:");

                foreach (var stat in stats.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key))
                {
                    Console.WriteLine("{0,5} resources of version {2} and type {1}{3}", stat.Value.Count, stat.Value.Type, stat.Value.Version,
                        stat.Value.Info != "" ? string.Format(" ({0})", stat.Value.Info) : ""
                    );
                }

                Console.WriteLine();
                Console.WriteLine("Unique special dependancies:");

                foreach (var stat in uniqueSpecialDependancies)
                {
                    Console.WriteLine("{0} in {1}", stat.Key, stat.Value);
                }
            }
        }

        private static void ProcessFile(string path)
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

        private static void ProcessFile(string path, Stream stream)
        {
            var resource = new Resource();

            try
            {
                var sw = Stopwatch.StartNew();

                resource.Read(stream);

                sw.Stop();

                Console.WriteLine("Parsed in {0}ms", sw.ElapsedMilliseconds);

                string extension = Path.GetExtension(path);

                if (extension.EndsWith("_c", StringComparison.Ordinal))
                {
                    extension = extension.Substring(0, extension.Length - 2);
                }

                // Verify that extension matches resource type
                if (resource.ResourceType != ResourceType.Unknown)
                {
                    var type = typeof(ResourceType).GetMember(resource.ResourceType.ToString()).First();
                    var attribute = "." + ((ExtensionAttribute)type.GetCustomAttributes(typeof(ExtensionAttribute), false).First()).Extension;

                    if (attribute != extension)
                    {
                        throw new Exception(string.Format("Mismatched resource type and file extension. ({0} != expected {1})", attribute, extension));
                    }
                }

                if (Options.CollectStats)
                {
                    string id = string.Format("{0}_{1}", resource.ResourceType, resource.Version);
                    string info = string.Empty;

                    switch(resource.ResourceType)
                    {
                        case ResourceType.Texture:
                            info = ((Texture)resource.Blocks[BlockType.DATA]).Format.ToString();
                            break;

                        case ResourceType.Sound:
                            info = ((Sound)resource.Blocks[BlockType.DATA]).Type.ToString();
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
                            stats[id].Count++;
                        }
                        else
                        {
                            stats.Add(id, new ResourceStat(resource, info));
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
                }

                if (Options.OutputFile != null)
                {
                    byte[] data;

                    switch (resource.ResourceType)
                    {
                        case ResourceType.Panorama:
                            data = ((Panorama)resource.Blocks[BlockType.DATA]).Data;
                            break;

                        case ResourceType.Sound:
                            var sound = ((Sound)resource.Blocks[BlockType.DATA]);

                            switch(sound.Type)
                            {
                                case Sound.AudioFileType.MP3:
                                    extension = "mp3";
                                    break;

                                case Sound.AudioFileType.WAV:
                                    extension = "wav";
                                    break;
                            }

                            data = sound.GetSound();

                            break;

                        case ResourceType.Texture:
                            extension = "png";

                            var bitmap = ((Texture)resource.Blocks[BlockType.DATA]).GenerateBitmap();

                            using (var ms = new MemoryStream())
                            {
                                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);

                                data = ms.ToArray();
                            }

                            break;
                        case ResourceType.Particle:
                        case ResourceType.Mesh:
                            //Wrap it around a KV3File object to get the header.
                            data = Encoding.UTF8.GetBytes(new ValveResourceFormat.KeyValues.KV3File(((BinaryKV3)resource.Blocks[BlockType.DATA]).Data).ToString());
                            break;

                        //These all just use ToString() and WriteText() to do the job
                        case ResourceType.SoundEventScript:
                            data = Encoding.UTF8.GetBytes(resource.Blocks[BlockType.DATA].ToString());
                            break;

                        default:
                            Console.WriteLine("-- (I don't know how to dump this resource type)");
                            return;
                    }

                    var filePath = Path.ChangeExtension(path, extension);

                    if (Options.RecursiveSearch)
                    {
                        // I bet this is prone to breaking, is there a better way?
                        filePath = filePath.Remove(0, Options.InputFile.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                    }
                    else
                    {
                        filePath = Path.GetFileName(filePath);
                    }

                    DumpFile(filePath, data);
                }
            }
            catch (Exception e)
            {
                lock (ConsoleWriterLock)
                {
                    File.AppendAllText("exceptions.txt", string.Format("---------------\nFile: {0}\nException: {1}\n\n", path, e));

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }
            }

            if (Options.CollectStats)
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

            if (Options.PrintAllBlocks || !string.IsNullOrEmpty(Options.BlockToPrint))
            {
                Console.WriteLine(Environment.NewLine);

                foreach (var block in resource.Blocks)
                {
                    if(!Options.PrintAllBlocks && Options.BlockToPrint != block.Key.ToString())
                    {
                        continue;
                    }

                    Console.WriteLine("--- Data for block \"{0}\" ---", block.Key);
                    Console.WriteLine(block.Value);
                }
            }
        }

        private static void ParseVCS(string path)
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

        private static void ParseVFont(string path)
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

                if (Options.OutputFile != null)
                {
                    var fileName = Path.GetFileName(path);
                    fileName = Path.ChangeExtension(fileName, "ttf");

                    DumpFile(fileName, output);
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
        
        private static void ParseVPK(string path)
        {
            lock (ConsoleWriterLock)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("--- Listing files in package \"{0}\" ---", path);
                Console.ResetColor();
            }

            var sw = Stopwatch.StartNew();

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

            if (Options.VerifyVPKChecksums)
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

            if (Options.OutputFile == null)
            {
                Console.WriteLine("--- Files in package:");

                var orderedEntries = package.Entries.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key);

                if (Options.CollectStats)
                {
                    TotalFiles += orderedEntries
                        .Where(entry => entry.Key.EndsWith("_c", StringComparison.Ordinal))
                        .Sum(x => x.Value.Count);
                }

                foreach (var entry in orderedEntries)
                {
                    Console.WriteLine("\t{0}: {1} files", entry.Key, entry.Value.Count);

                    if (Options.CollectStats && entry.Key.EndsWith("_c", StringComparison.Ordinal))
                    {
                        foreach (var file in entry.Value)
                        {
                            lock (ConsoleWriterLock)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("[{0}/{1}] {2}", ++CurrentFile, TotalFiles, file.GetFullPath());
                                Console.ResetColor();
                            }

                            byte[] output;
                            package.ReadEntry(file, out output);

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

                DumpVPK(package, "vxml_c", "xml");
                DumpVPK(package, "vjs_c", "js");
                DumpVPK(package, "vcss_c", "css");
                DumpVPK(package, "vsndevts_c", "vsndevts");
                DumpVPK(package, "vpcf_c", "vpcf");

                DumpVPK(package, "txt", "txt");
                DumpVPK(package, "cfg", "cfg");
                DumpVPK(package, "res", "res");
            }

            if (Options.OutputVPKDir)
            {
                foreach (var type in package.Entries)
                {
                    foreach (var file in type.Value)
                    {
                        Console.WriteLine(file);
                    }
                }
            }

            sw.Stop();

            Console.WriteLine("Processed in {0}ms", sw.ElapsedMilliseconds);
        }

        private static void DumpVPK(Package package, string type, string newType)
        {
            if (!package.Entries.ContainsKey(type))
            {
                Console.WriteLine("There are no files of type \"{0}\".", type);

                return;
            }

            var entries = package.Entries[type];

            foreach (var file in entries)
            {
                var filePath = string.Format("{0}.{1}", file.FileName, file.TypeName);

                if (!string.IsNullOrWhiteSpace(file.DirectoryName))
                {
                    filePath = Path.Combine(file.DirectoryName, filePath);
                }

                filePath = FixPathSlahes(filePath);

                Console.WriteLine("\t[archive index: {0:D3}] {1}", file.ArchiveIndex, filePath);

                byte[] output;
                package.ReadEntry(file, out output);

                if (type.EndsWith("_c", StringComparison.Ordinal))
                {
                    using (var resource = new Resource())
                    {
                        using (var memory = new MemoryStream(output))
                        {
                            resource.Read(memory);
                        }
                        switch(type)
                        {
                            case "vxml_c":
                            case "vcss_c":
                            case "vjs_c":
                                output = ((Panorama)resource.Blocks[BlockType.DATA]).Data;
                                break;
                            default:
                                output = Encoding.UTF8.GetBytes(resource.Blocks[BlockType.DATA].ToString());
                                break;
                        }
                        
                    }
                }

                if (Options.OutputFile != null)
                {
                    if (type != newType)
                    {
                        filePath = Path.ChangeExtension(filePath, newType);
                    }

                    DumpFile(filePath, output);
                }
            }
        }

        private static void DumpFile(string path, byte[] data)
        {
            var outputFile = Path.Combine(Options.OutputFile, path);

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

            File.WriteAllBytes(outputFile, data);

            Console.WriteLine("--- Dump written to \"{0}\"", outputFile);
        }

        private static string FixPathSlahes(string path)
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
