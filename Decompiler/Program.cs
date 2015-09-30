using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Decompiler
{
    class Decompiler
    {
        private static Options Options;

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

            foreach (var path in paths)
            {
                if (Path.GetExtension(path) == ".vpk")
                {
                    ParseVPK(path);

                    continue;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("--- Info for resource file \"{0}\" ---", path);
                Console.ResetColor();

                var resource = new Resource();

                try
                {
                    var sw = Stopwatch.StartNew();

                    resource.Read(path);

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

                    if (Options.OutputFile != null)
                    {
                        byte[] data;

                        switch (resource.ResourceType)
                        {
                            case ResourceType.Panorama:
                                data = ((Panorama)resource.Blocks[BlockType.DATA]).Data;
                                break;

                            case ResourceType.Sound:
                                extension = "mp3";
                                data = ((Sound)resource.Blocks[BlockType.DATA]).SoundData;
                                break;

                            default:
                                Console.WriteLine("-- (I don't know how to dump this resource type)");
                                continue;
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
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(e);
                    Console.ResetColor();
                }

                //Console.WriteLine("\tInput Path: \"{0}\"", args[fi]);
                //Console.WriteLine("\tResource Name: \"{0}\"", "???");
                //Console.WriteLine("\tID: {0:x16}", 0);

                // Highlight resource type line if undetermined
                if (resource.ResourceType == ResourceType.Unknown)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                }

                Console.WriteLine("\tResource Type: {0} [Version {1}] [Header Version: {2}]", resource.ResourceType, resource.Version, resource.HeaderVersion);
                Console.ResetColor();
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

                // Print blocks
                Console.WriteLine("--- Resource Blocks: Count {0} ---", resource.Blocks.Count);

                foreach (var block in resource.Blocks)
                {
                    Console.WriteLine("\t-- Block: {0,-4}  Size: {1,-6} bytes [Offset: {2,6}]", block.Key, block.Value.Size, block.Value.Offset);
                }

                Console.WriteLine(Environment.NewLine);

                foreach (var block in resource.Blocks)
                {
                    Console.WriteLine("--- Data for block \"{0}\" ---", block.Key);
                    Console.WriteLine(block.Value);
                }
            }
        }

        private static void ParseVPK(string path)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("--- Listing files in package \"{0}\" ---", path);
            Console.ResetColor();

            var sw = Stopwatch.StartNew();

            var package = new Package();

            try
            {
                package.Read(path);
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(e);
                Console.ResetColor();
            }

            if (Options.OutputFile == null)
            {
                Console.WriteLine("--- Files in package:");

                var orderedEntries = package.Entries.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key);

                foreach (var entry in orderedEntries)
                {
                    Console.WriteLine("\t{0}: {1} files", entry.Key, entry.Value.Count);
                }
            }
            else
            {
                Console.WriteLine("--- Dumping decompiled files...");

                DumpVPK(package, "vxml_c", "xml");
                DumpVPK(package, "vjs_c", "js");
                DumpVPK(package, "vcss_c", "css");

                DumpVPK(package, "txt", "txt");
                DumpVPK(package, "cfg", "cfg");
                DumpVPK(package, "res", "res");
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
                    var resource = new Resource();
                    using (var memory = new MemoryStream(output))
                    {
                        resource.Read(memory);
                    }

                    output = ((Panorama)resource.Blocks[BlockType.DATA]).Data;
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
