using System;
using System.Collections.Generic;
using System.IO;
using ValveResourceFormat;

namespace Decompiler
{
    class Decompiler
    {
        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: decompiler <path>");
                Console.WriteLine("\tPath can be a single file, a list of files, or a directory.");

                return;
            }

            var paths = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                var path = Path.GetFullPath(args[i]);

                if (Directory.Exists(path))
                {
                    Console.WriteLine("Will read all files in \"{0}\"", path);

                    paths.AddRange(Directory.GetFiles(path));

                    continue;
                }
                else if (File.Exists(path))
                {
                    paths.Add(path);

                    continue;
                }

                throw new FileNotFoundException(string.Format("No such file \"{0}\"", path));
            }

            Console.WriteLine("Found {0} files to read", paths.Count);
            Console.WriteLine();

            foreach (var path in paths)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("--- Info for resource file \"{0}\" ---", path);
                Console.ResetColor();

                var resource = new Resource();

                try
                {
                    resource.ResourceType = ResourceType.Model; // TODO: get rid of this
                    resource.Read(path);
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
                Console.WriteLine("\tResource Type: {0} = {1} (0x{2:X8}) [Version {3}] [Header Version: {4}]", "???", 0, 0, resource.Version, resource.HeaderVersion);
                Console.WriteLine("\tFile Size: {0} bytes", resource.FileSize);

                Console.WriteLine(Environment.NewLine);

                if (resource.Blocks.ContainsKey(BlockType.RERL))
                {
                    Console.WriteLine("--- Resource External Refs: ---");
                    Console.WriteLine("\t{0,-16}  {1,-48}", "Id:", "Resource Name:");

                    foreach (var res in ((ValveResourceFormat.Blocks.ResourceExtRefList)resource.Blocks[BlockType.RERL]).ResourceRefInfoList)
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
                        continue;
                    }

                    Console.WriteLine("--- Data for block \"{0}\" ---", block.Key);
                    Console.WriteLine(block.Value);
                }
            }
        }
    }
}
