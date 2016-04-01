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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ValveResourceFormat
{
    public class Package : IDisposable
    {
        public const int MAGIC = 0x55AA1234;

        /// <summary>
        /// Always '/' as per Valve's vpk implementation.
        /// </summary>
        public const char DirectorySeparatorChar = '/';

        private BinaryReader Reader;
        private bool IsDirVPK;
        private uint HeaderSize;

        /// <summary>
        /// Gets the File Name
        /// </summary>
        public string FileName { get; private set; }

        /// <summary>
        /// Gets the VPK version.
        /// </summary>
        public uint Version { get; private set; }

        /// <summary>
        /// Gets the size in bytes of the directory tree.
        /// </summary>
        public uint TreeSize { get; private set; }

        /// <summary>
        /// Gets how many bytes of file content are stored in this VPK file (0 in CSGO).
        /// </summary>
        public uint FileDataSectionSize { get; private set; }

        /// <summary>
        /// Gets the size in bytes of the section containing MD5 checksums for external archive content.
        /// </summary>
        public uint ArchiveMD5SectionSize { get; private set; }

        /// <summary>
        /// Gets the size in bytes of the section containing MD5 checksums for content in this file.
        /// </summary>
        public uint OtherMD5SectionSize { get; private set; }

        /// <summary>
        /// Gets the size in bytes of the section containing the public key and signature.
        /// </summary>
        public uint SignatureSectionSize { get; private set; }

        /// <summary>
        /// Gets the MD5 checksum of the file tree.
        /// </summary>
        public byte[] TreeChecksum { get; private set; }

        /// <summary>
        /// Gets the MD5 checksum of the archive MD5 checksum section entries.
        /// </summary>
        public byte[] ArchiveMD5EntriesChecksum { get; private set; }

        /// <summary>
        /// Gets the MD5 checksum of the complete package until the signature structure.
        /// </summary>
        public byte[] WholeFileChecksum { get; private set; }

        /// <summary>
        /// Gets the public key.
        /// </summary>
        public byte[] PublicKey { get; private set; }

        /// <summary>
        /// Gets the signature.
        /// </summary>
        public byte[] Signature { get; private set; }

        /// <summary>
        /// Gets the package entries.
        /// </summary>
        public Dictionary<string, List<PackageEntry>> Entries { get; private set; }

        /// <summary>
        /// Gets the archive MD5 checksum section entries. Also known as cache line hashes.
        /// </summary>
        public List<ArchiveMD5SectionEntry> ArchiveMD5Entries { get; private set; }

        /// <summary>
        /// Releases binary reader.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && Reader != null)
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
                IsDirVPK = true;

                fileName = fileName.Substring(0, fileName.Length - 4);
            }

            FileName = fileName;
        }

        /// <summary>
        /// Opens and reads the given filename.
        /// The file is held open until the object is disposed.
        /// </summary>
        /// <param name="filename">The file to open and read.</param>
        public void Read(string filename)
        {
            SetFileName(filename);

            var fs = new FileStream($"{FileName}{(IsDirVPK ? "_dir" : string.Empty)}.vpk", FileMode.Open, FileAccess.Read, FileShare.Read);

            Read(fs);
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

            if (Reader.ReadUInt32() != MAGIC)
            {
                throw new InvalidDataException("Given file is not a VPK.");
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

            HeaderSize = (uint)input.Position;

            ReadEntries();

            if (Version == 2)
            {
                // Skip over file data, if any
                input.Position += FileDataSectionSize;

                ReadArchiveMD5Section();
                ReadOtherMD5Section();
                ReadSignatureSection();
            }
        }

        /// <summary>
        /// Searches for a given file entry in the file list.
        /// </summary>
        /// <param name="filePath">Full path to the file to find.</param>
        public PackageEntry FindEntry(string filePath)
        {
            filePath = filePath?.Replace('\\', DirectorySeparatorChar);

            // Even though technically we are passing in full path as file name, relevant functions in next overload fix it
            return FindEntry(Path.GetDirectoryName(filePath), filePath);
        }

        /// <summary>
        /// Searches for a given file entry in the file list.
        /// </summary>
        /// <param name="directory">Directory to search in.</param>
        /// <param name="fileName">File name to find.</param>
        public PackageEntry FindEntry(string directory, string fileName)
        {
            fileName = fileName?.Replace('\\', DirectorySeparatorChar);

            return FindEntry(directory, Path.GetFileNameWithoutExtension(fileName), Path.GetExtension(fileName)?.TrimStart('.'));
        }

        /// <summary>
        /// Searches for a given file entry in the file list.
        /// </summary>
        /// <param name="directory">Directory to search in.</param>
        /// <param name="fileName">File name to find, without the extension.</param>
        /// <param name="extension">File extension, without the leading dot.</param>
        public PackageEntry FindEntry(string directory, string fileName, string extension)
        {
            // Assume no extension
            if (extension == null)
            {
                extension = string.Empty;
            }

            if (!Entries.ContainsKey(extension))
            {
                return null;
            }

            // We normalize path separators when reading the file list
            directory = directory?.Replace('\\', DirectorySeparatorChar).Trim(DirectorySeparatorChar);

            // If the directory is empty after trimming, set it to null
            if (directory == string.Empty)
            {
                directory = null;
            }

            return Entries[extension].FirstOrDefault(x => x.DirectoryName == directory && x.FileName == fileName);
        }

        /// <summary>
        /// Reads the entry from the VPK package.
        /// </summary>
        /// <param name="entry">Package entry.</param>
        /// <param name="output">Output buffer.</param>
        /// <param name="validateCrc">If true, CRC32 will be calculated and verified for read data.</param>
        public void ReadEntry(PackageEntry entry, out byte[] output, bool validateCrc = true)
        {
            output = new byte[entry.SmallData.Length + entry.Length];

            if (entry.SmallData.Length > 0)
            {
                entry.SmallData.CopyTo(output, 0);
            }

            if (entry.Length > 0)
            {
                Stream fs = null;

                try
                {
                    var offset = entry.Offset;

                    if (entry.ArchiveIndex != 0x7FFF)
                    {
                        if (!IsDirVPK)
                        {
                            throw new InvalidOperationException("Given VPK is not a _dir, but entry is referencing an external archive.");
                        }

                        var fileName = $"{FileName}_{entry.ArchiveIndex:D3}.vpk";

                        fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                    }
                    else
                    {
                        fs = Reader.BaseStream;

                        offset += HeaderSize + TreeSize;
                    }

                    fs.Seek(offset, SeekOrigin.Begin);
                    fs.Read(output, entry.SmallData.Length, (int)entry.Length);
                }
                finally
                {
                    if (entry.ArchiveIndex != 0x7FFF)
                    {
                        fs?.Close();
                    }
                }
            }

            if (validateCrc && entry.CRC32 != Crc32.Compute(output))
            {
                throw new InvalidDataException("CRC32 mismatch for read data.");
            }
        }

        private void ReadEntries()
        {
            var typeEntries = new Dictionary<string, List<PackageEntry>>();

            // Types
            while (true)
            {
                var typeName = Reader.ReadNullTermString(Encoding.UTF8);

                if (typeName == string.Empty)
                {
                    break;
                }

                // Valve uses a space for missing extensions,
                // we replace it with an empty string to match how System.IO.Path deals with it.
                if (typeName == " ")
                {
                    typeName = string.Empty;
                }

                var entries = new List<PackageEntry>();

                // Directories
                while (true)
                {
                    var directoryName = Reader.ReadNullTermString(Encoding.UTF8);

                    if (directoryName == string.Empty)
                    {
                        break;
                    }

                    // Valve uses a space for blank directory names,
                    // we replace it with a null to match how System.IO.Path deals with root paths.
                    if (directoryName == " ")
                    {
                        directoryName = null;
                    }

                    // Files
                    while (true)
                    {
                        var fileName = Reader.ReadNullTermString(Encoding.UTF8);

                        if (fileName == string.Empty)
                        {
                            break;
                        }

                        var entry = new PackageEntry
                        {
                            FileName = fileName,
                            DirectoryName = directoryName,
                            TypeName = typeName,
                            CRC32 = Reader.ReadUInt32(),
                            SmallData = new byte[Reader.ReadUInt16()],
                            ArchiveIndex = Reader.ReadUInt16(),
                            Offset = Reader.ReadUInt32(),
                            Length = Reader.ReadUInt32()
                        };

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
        /// Verify checksums and signatures provided in the VPK
        /// </summary>
        public void VerifyHashes()
        {
            if (Version != 2)
            {
                throw new InvalidDataException("Only version 2 is supported.");
            }

            using (var md5 = MD5.Create())
            {
                Reader.BaseStream.Position = 0;

                var hash = md5.ComputeHash(Reader.ReadBytes((int)(HeaderSize + TreeSize + FileDataSectionSize + ArchiveMD5SectionSize + 32)));

                if (!hash.SequenceEqual(WholeFileChecksum))
                {
                    throw new InvalidDataException($"Package checksum mismatch ({BitConverter.ToString(hash)} != expected {BitConverter.ToString(WholeFileChecksum)})");
                }

                Reader.BaseStream.Position = HeaderSize;

                hash = md5.ComputeHash(Reader.ReadBytes((int)TreeSize));

                if (!hash.SequenceEqual(TreeChecksum))
                {
                    throw new InvalidDataException($"File tree checksum mismatch ({BitConverter.ToString(hash)} != expected {BitConverter.ToString(TreeChecksum)})");
                }

                Reader.BaseStream.Position = HeaderSize + TreeSize + FileDataSectionSize;

                hash = md5.ComputeHash(Reader.ReadBytes((int)ArchiveMD5SectionSize));

                if (!hash.SequenceEqual(ArchiveMD5EntriesChecksum))
                {
                    throw new InvalidDataException($"Archive MD5 entries checksum mismatch ({BitConverter.ToString(hash)} != expected {BitConverter.ToString(ArchiveMD5EntriesChecksum)})");
                }

                // TODO: verify archive checksums
            }

            if (PublicKey == null || Signature == null)
            {
                return;
            }

            if (!IsSignatureValid())
            {
                throw new InvalidDataException("VPK signature is not valid.");
            }
        }

        /// <summary>
        /// Verifies the RSA signature.
        /// </summary>
        /// <returns>True if signature is valid, false otherwise.</returns>
        public bool IsSignatureValid()
        {
            Reader.BaseStream.Position = 0;

            var keyParser = new ThirdParty.AsnKeyParser(PublicKey);

            var rsa = new RSACryptoServiceProvider();
            rsa.ImportParameters(keyParser.ParseRSAPublicKey());

            var deformatter = new RSAPKCS1SignatureDeformatter(rsa);
            deformatter.SetHashAlgorithm("SHA256");

            var hash = new SHA256Managed().ComputeHash(Reader.ReadBytes((int)(HeaderSize + TreeSize + FileDataSectionSize + ArchiveMD5SectionSize + OtherMD5SectionSize)));

            return deformatter.VerifySignature(hash, Signature);
        }

        private void ReadArchiveMD5Section()
        {
            ArchiveMD5Entries = new List<ArchiveMD5SectionEntry>();

            if (ArchiveMD5SectionSize == 0)
            {
                return;
            }

            var entries = ArchiveMD5SectionSize / 28; // 28 is sizeof(VPK_MD5SectionEntry), which is int + int + int + 16 chars

            for (var i = 0; i < entries; i++)
            {
                ArchiveMD5Entries.Add(new ArchiveMD5SectionEntry
                {
                    ArchiveIndex = Reader.ReadUInt32(),
                    Offset = Reader.ReadUInt32(),
                    Length = Reader.ReadUInt32(),
                    Checksum = Reader.ReadBytes(16)
                });
            }
        }

        private void ReadOtherMD5Section()
        {
            if (OtherMD5SectionSize != 48)
            {
                throw new InvalidDataException($"Encountered OtherMD5Section with size of {OtherMD5SectionSize} (should be 48)");
            }

            TreeChecksum = Reader.ReadBytes(16);
            ArchiveMD5EntriesChecksum = Reader.ReadBytes(16);
            WholeFileChecksum = Reader.ReadBytes(16);
        }

        private void ReadSignatureSection()
        {
            if (SignatureSectionSize == 0)
            {
                return;
            }

            var publicKeySize = Reader.ReadInt32();
            PublicKey = Reader.ReadBytes(publicKeySize);

            var signatureSize = Reader.ReadInt32();
            Signature = Reader.ReadBytes(signatureSize);
        }
    }
}
