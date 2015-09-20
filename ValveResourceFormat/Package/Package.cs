/*
 * Read() function was mostly taken from Rick's Gibbed.Valve.FileFormats,
 * which is subject to this license:
 *
 * Copyright (c) 2008 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 * claim that you wrote the original software. If you use this software
 * in a product, an acknowledgment in the product documentation would be
 * appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not be
 * misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 * distribution.
 */

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace ValveResourceFormat
{
    public class Package
    {
        public const int MAGIC = 0x55AA1234;

        private BinaryReader Reader;
        private string FileName;

        /// <summary>
        /// Gets the VPK version.
        /// </summary>
        public uint Version { get; private set; }

        /// <summary>
        /// The size, in bytes, of the directory tree.
        /// </summary>
        public uint TreeSize { get; private set; }

        /// <summary>
        /// How many bytes of file content are stored in this VPK file (0 in CSGO).
        /// </summary>
        public uint FileDataSectionSize { get; private set; }

        /// <summary>
        /// The size, in bytes, of the section containing MD5 checksums for external archive content.
        /// </summary>
        public uint ArchiveMD5SectionSize { get; private set; }

        /// <summary>
        /// The size, in bytes, of the section containing MD5 checksums for content in this file.
        /// </summary>
        public uint OtherMD5SectionSize { get; private set; }

        /// <summary>
        /// The size, in bytes, of the section containing the public key and signature.
        /// </summary>
        public uint SignatureSectionSize { get; private set; }

        /// <summary>
        /// Package entries.
        /// </summary>
        public Dictionary<string, List<PackageEntry>> Entries { get; private set; }

        /// <summary>
        /// Releases binary reader.
        /// </summary>
        ~Package()
        {
            if (Reader != null)
            {
                Reader.Dispose();

                Reader = null;
            }
        }

        /// <summary>
        /// Sets the file name.
        /// </summary>
        /// <param name="fileName">Filename.</param>
        public void SetFileName(string fileName)
        {
            if (fileName.EndsWith(".vpk", StringComparison.Ordinal))
            {
                fileName = fileName.Substring(0, fileName.Length - 4);
            }

            if (fileName.EndsWith("_dir", StringComparison.Ordinal))
            {
                fileName = fileName.Substring(0, fileName.Length - 4);
            }

            FileName = fileName;

            fileName += "_dir.vpk";

            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException(string.Format("\"{0}\" does not exist.", fileName));
            }
        }

        /// <summary>
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(Stream input)
        {
            if (FileName == null)
            {
                throw new InvalidOperationException("If you call Read() directly with a stream, you must call SetFileName() first.");
            }

            Reader = new BinaryReader(input);

            if (Reader.ReadUInt32() != Package.MAGIC)
            {
                throw new InvalidDataException("Given file is not a VPK dictionary file.");
            }

            Version = Reader.ReadUInt32();
            TreeSize = Reader.ReadUInt32();

            if (Version == 1)
            {
                // Nothing else
            }
            else if (Version == 2)
            {
                FileDataSectionSize = Reader.ReadUInt32();
                ArchiveMD5SectionSize = Reader.ReadUInt32();
                OtherMD5SectionSize = Reader.ReadUInt32();
                SignatureSectionSize = Reader.ReadUInt32();
            }
            else
            {
                throw new InvalidDataException(string.Format("Bad VPK version. ({0})", Version));
            }

            var typeEntries = new Dictionary<string, List<PackageEntry>>();

            // Types
            while (true)
            {
                string typeName = Reader.ReadNullTermString(Encoding.UTF8);

                if (typeName == "")
                {
                    break;
                }

                var entries = new List<PackageEntry>();

                // Directories
                while (true)
                {
                    string directoryName = Reader.ReadNullTermString(Encoding.UTF8);

                    if (directoryName == "")
                    {
                        break;
                    }

                    // Files
                    while (true)
                    {
                        string fileName = Reader.ReadNullTermString(Encoding.UTF8);

                        if (fileName == "")
                        {
                            break;
                        }

                        var entry = new PackageEntry();
                        entry.FileName = fileName;
                        entry.DirectoryName = directoryName.Replace("/", "\\");
                        entry.TypeName = typeName;
                        entry.CRC32 = Reader.ReadUInt32();
                        entry.SmallData = new byte[Reader.ReadUInt16()];
                        entry.ArchiveIndex = Reader.ReadUInt16();
                        entry.Offset = Reader.ReadUInt32();
                        entry.Length = Reader.ReadUInt32();

                        if (Reader.ReadUInt16() != 0xFFFF)
                        {
                            throw new FormatException("Invalid terminator.");
                        }

                        if (entry.SmallData.Length > 0)
                        {
                            Reader.Read(entry.SmallData, 0, entry.SmallData.Length);
                        }

                        entries.Add(entry);
                    }
                }

                typeEntries.Add(typeName, entries);
            }

            Entries = typeEntries;
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            SetFileName(filename);

            using (var fs = new FileStream(FileName + "_dir.vpk", FileMode.Open, FileAccess.Read))
            {
                Read(fs);
            }
        }

        /// <summary>
        /// Reads the entry from the VPK package.
        /// </summary>
        /// <param name="entry">Package entry.</param>
        /// <param name="output">Output buffer.</param>
        public void ReadEntry(PackageEntry entry, out byte[] output)
        {
            if (entry.ArchiveIndex == 0x7FFF)
            {
                throw new NotImplementedException("Inline file in vpk, not yet handled.");
            }

            if (entry.SmallData.Length > 0)
            {
                throw new NotImplementedException("SmallData.Length > 0, not yet handled.");
            }

            output = new byte[entry.Length];

            var fileName = string.Format("{0}_{1:D3}.vpk", FileName, entry.ArchiveIndex);

            using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read))
            {
                fs.Seek(entry.Offset, SeekOrigin.Begin);
                fs.Read(output, 0, (int)entry.Length);
            }
        }
    }
}
