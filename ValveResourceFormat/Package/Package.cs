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
        public List<PackageEntry> Entries { get; private set; }

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
        /// Reads the given <see cref="Stream"/>.
        /// </summary>
        /// <param name="input">The input <see cref="Stream"/> to read from.</param>
        public void Read(Stream input)
        {
            Reader = new BinaryReader(input);

            if (Reader.ReadUInt32() != Package.MAGIC)
            {
                throw new InvalidDataException("Given file is not a VPK dictionary file.");
            }

            var version = Reader.ReadUInt32();
            TreeSize = Reader.ReadUInt32();

            if (version == 1)
            {
                // Nothing else
            }
            else if (version == 2)
            {
                FileDataSectionSize = Reader.ReadUInt32();
                ArchiveMD5SectionSize = Reader.ReadUInt32();
                OtherMD5SectionSize = Reader.ReadUInt32();
                SignatureSectionSize = Reader.ReadUInt32();
            }
            else
            {
                throw new InvalidDataException(string.Format("Bad VPK version. ({0})", version));
            }

            var entries = new List<PackageEntry>();

            // Types
            while (true)
            {
                string typeName = Reader.ReadNullTermString(Encoding.UTF8);
                if (typeName == "")
                {
                    break;
                }

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
                        entry.Size = Reader.ReadUInt32();

                        ushort terminator = Reader.ReadUInt16();

                        if (terminator != 0xFFFF)
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
            }

            Entries = entries;
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                Read(fs);
            }
        }
    }
}
