using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.ToolsAssetInfo
{
    public class ToolsAssetInfo
    {
        public const uint MAGIC = 0xC4CCACE8;
        public const uint MAGIC2 = 0xC4CCACE9; // TODO: Versioning
        public const uint GUARD = 0x049A48B2;

        public List<string> Mods { get; } = new List<string>();
        public List<string> Directories { get; } = new List<string>();
        public List<string> Filenames { get; } = new List<string>();
        public List<string> Extensions { get; } = new List<string>();
        public List<string> EditInfoKeys { get; } = new List<string>();
        public List<string> MiscStrings { get; } = new List<string>();
        public List<string> ConstructedFilepaths { get; } = new List<string>();

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            Read(fs);
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(Stream input)
        {
            var reader = new BinaryReader(input);
            var magic = reader.ReadUInt32();

            // TODO: Versioning
            if (magic != MAGIC && magic != MAGIC2)
            {
                throw new InvalidDataException("Given file is not tools_asset_info.");
            }

            var version = reader.ReadUInt32();

            if (version != 9 && version != 10 && version != 11)
            {
                throw new InvalidDataException($"Unsupported version: {version}");
            }

            var fileCount = reader.ReadUInt32();
            var b = reader.ReadUInt32(); // block id?

            if (b != 1)
            {
                throw new InvalidDataException($"b is {b}");
            }

            ReadStringsBlock(reader, Mods);
            ReadStringsBlock(reader, Directories);
            ReadStringsBlock(reader, Filenames);
            ReadStringsBlock(reader, Extensions);
            ReadStringsBlock(reader, EditInfoKeys);
            ReadStringsBlock(reader, MiscStrings);

            for (var i = 0; i < fileCount; i++)
            {
                var hash = reader.ReadUInt64();

                var unk1 = (int)(hash >> 61) & 7;
                var addonIndex = (int)(hash >> 52) & 0x1FF;
                var directoryIndex = (int)(hash >> 33) & 0x7FFFF;
                var filenameIndex = (int)(hash >> 10) & 0x7FFFFF;
                var extensionIndex = (int)(hash & 0x3FF);

                //Console.WriteLine($"{unk1} {addonIndex} {directoryIndex} {filenameIndex} {extensionIndex}");

                var path = new StringBuilder();

                if (addonIndex != 0x1FF)
                {
                    path.Append(Mods[addonIndex]);
                    path.Append('/');
                }

                if (directoryIndex != 0x7FFFF)
                {
                    path.Append(Directories[directoryIndex]);
                    path.Append('/');
                }

                if (filenameIndex != 0x7FFFFF)
                {
                    path.Append(Filenames[filenameIndex]);
                }

                if (extensionIndex != 0x3FF)
                {
                    path.Append('.');
                    path.Append(Extensions[extensionIndex]);
                }

                ConstructedFilepaths.Add(path.ToString());
            }
        }

        private static void ReadStringsBlock(BinaryReader reader, ICollection<string> output)
        {
            var count = reader.ReadUInt32();

            for (uint i = 0; i < count; i++)
            {
                output.Add(reader.ReadNullTermString(Encoding.UTF8));
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            foreach (var str in ConstructedFilepaths)
            {
                sb.AppendLine(str);
            }

            return sb.ToString();
        }
    }
}
