using System;
using System.Collections.Generic;
using System.IO;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using System.Diagnostics;

namespace Decompiler
{
    class Decompiler
    {
        public static void Main(string[] args)
        {
            var options = new Options();
            CommandLine.Parser.Default.ParseArgumentsStrict(args, options);

            options.InputFile = Path.GetFullPath(options.InputFile);

            if (options.OutputFile != null)
            {
                options.OutputFile = Path.GetFullPath(options.OutputFile);
            }

            var paths = new List<string>();

            if (Directory.Exists(options.InputFile))
            {
                if (options.OutputFile != null && File.Exists(options.OutputFile))
                {
                    Console.Error.WriteLine("Output path is an existing file, but input is a folder.");

                    return;
                }

                paths.AddRange(Directory.GetFiles(options.InputFile, "*.*_c", options.RecursiveSearch ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
            }
            else if (File.Exists(options.InputFile))
            {
                options.RecursiveSearch = false;

                paths.Add(options.InputFile);
            }

            if (paths.Count == 0)
            {
                Console.Error.WriteLine("No such file \"{0}\" or directory is empty. Did you mean to include --recursive parameter?", options.InputFile);

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

                    if(options.OutputFile != null)
                    {
                        if (resource.ResourceType != ResourceType.Panorama)
                        {
                            Console.Error.WriteLine("--- (We only support dumping panorama resources at the moment.)");

                            continue;
                        }

                        var outputFile = options.OutputFile;

                        if(options.RecursiveSearch)
                        {
                            // I bet this is prone to breaking, is there a better way?
                            outputFile = Path.Combine(outputFile, Path.GetDirectoryName(path.Remove(0, options.InputFile.TrimEnd(Path.DirectorySeparatorChar).Length + 1)));

                            if (!Directory.Exists(outputFile))
                            {
                                Directory.CreateDirectory(outputFile);
                            }
                        }

                        if (Directory.Exists(outputFile))
                        {
                            outputFile = Path.Combine(outputFile, Path.GetFileNameWithoutExtension(path) + Path.GetExtension(path).Replace("_c", ""));
                        }

                        File.WriteAllBytes(outputFile, ((Panorama)resource.Blocks[BlockType.DATA]).Data);

                        Console.WriteLine("--- Dump written to \"{0}\"", outputFile);
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
                    Console.WriteLine("\t-- Block: {0,-4}  Size: {1,-6} bytes", block.Key, block.Value.Size);
                }

                Console.WriteLine(Environment.NewLine);

                foreach (var block in resource.Blocks)
                {
                    if (block.Key == BlockType.DATA)
                    {
                        if (resource.ResourceType != ResourceType.Panorama)
                        {
                            continue;
                        }
                    }

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

            foreach (var entry in package.Entries)
            {
                Console.WriteLine("\t[archive index: {2}] {0}\\{1}", entry.DirectoryName, entry.FileName, entry.ArchiveIndex);
            }
        }
    }
}
