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
                Console.WriteLine("\tResource Type: {0} = {1} (0x{2:x8}) [Version {3}] [Header Version: {4}]", "???", 0, 0, resource.Version, resource.HeaderVersion);
                Console.WriteLine("\tFile Size: {0} bytes", resource.FileSize);

                Console.WriteLine(Environment.NewLine);

                // Print blocks first
                Console.WriteLine("--- Resource Blocks: Count {0} ---", resource.Blocks.Count);

                foreach (var block in resource.Blocks)
                {
                    Console.WriteLine("\t-- Block: {0,-4}\tSize: {1}\tbytes   Offset: {2}", block.Key, block.Value.Size, block.Value.Offset);
                }

                Console.WriteLine(Environment.NewLine);

                // Print each block and their contents now
                foreach (var block in resource.Blocks)
                {
                    switch (block.Key)
                    {
                        case BlockType.RERL:
                            Console.WriteLine("--- Resource External Refs: ---");
                            Console.WriteLine("\t{0,-16}  Resource Name:", "Id:");

                            foreach (var res in ((ValveResourceFormat.Blocks.ResourceExtRefList)block.Value).ResourceRefInfoList)
                            {
                                Console.WriteLine("\t{0,-16}  {1}", HexMe(res.Id), res.Name);
                            }

                            break;

                        case BlockType.REDI:
                            Console.WriteLine("--- ResourceEditInfoBlock_t ---");

                            foreach (var res in ((ValveResourceFormat.Blocks.ResourceEditInfo)block.Value).Structs)
                            {
                                Console.WriteLine(res);
                            }

                            break;

                        default:
                            Console.WriteLine("Don't know how to handle {0}", block.Key);
                            break;
                    }

                    Console.WriteLine();
                }

                Console.WriteLine();
            }
        }

        private static string HexMe(ulong input)
        {
            var hexString = input.ToString("X");

            return (hexString.Length % 2 == 0 ? "" : "0") + hexString;
        }
    }
}
