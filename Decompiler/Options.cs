using System;
using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

namespace Decompiler
{
    class Options
    {
        [Option('i', "input", Required = true,
            HelpText = "Input file to be processed. With no additional arguments, a summary of the input(s) will be displayed.")]
        public string InputFile { get; set; }

        [Option("recursive", DefaultValue = false,
            HelpText = "If specified and given input is a folder, all sub directories will be scanned too.")]
        public bool RecursiveSearch { get; set; }

        [Option('o', "output",
            HelpText = "Writes DATA output to file.")]
        public string OutputFile { get; set; }

        [Option('a', "all",
            HelpText = "Prints the content of each resource block in the file.")]
        public bool PrintAllBlocks { get; set; }

        [Option('b', "block",
            HelpText = "Print the content of a specific block. Specify the block via its 4CC name - case matters! (eg. DATA, RERL, REDI, NTRO).")]
        public string BlockToPrint { get; set; }

        [Option("stats", DefaultValue = false,
            HelpText = "Collect stats on all input files and then print them.")]
        public bool CollectStats { get; set; }

        [Option("threads", DefaultValue = 1,
            HelpText = "If more than 1, files will be processed concurrently.")]
        public int MaxParallelismThreads { get; set; }

        [Option("vpk_dir", DefaultValue = false,
            HelpText = "Write a file with files in given VPK and their CRC.")]
        public bool OutputVPKDir { get; set; }

        [Option("vpk_verify", DefaultValue = false,
            HelpText = "Verify checksums and signatures.")]
        public bool VerifyVPKChecksums { get; set; }

        [Option("vpk_cache", DefaultValue = false,
            HelpText = "Use cached VPK manifest to keep track of updates. Only changed files will be written to disk.")]
        public bool CachedManifest { get; set; }

        [Option('d', "vpk_decompile", DefaultValue = false,
            HelpText = "Decompile supported files")]
        public bool Decompile { get; set; }

        [OptionList('e', "vpk_extensions", Separator = ',', DefaultValue = new string[] { },
            HelpText = "File extension(s) filter, example: \"vcss_c,vjs_c,vxml_c\"")]
        public IList<string> ExtFilter { get; set; }

        [Option('f', "vpk_filepath",
            HelpText = "File path filter, example: panorama\\ or \"panorama\\\\\"")]
        public string FileFilter { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this, current => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }
}
