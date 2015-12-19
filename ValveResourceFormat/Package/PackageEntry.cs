using System;
using System.IO;

namespace ValveResourceFormat
{
    public class PackageEntry
    {
        /// <summary>
        /// Gets or sets file name of this entry.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the name of the directory this file is in.
        /// </summary>
        public string DirectoryName { get; set; }

        /// <summary>
        /// Gets or sets the file extension.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Gets or sets the CRC32 checksum of this entry.
        /// </summary>
        public uint CRC32 { get; set; }

        /// <summary>
        /// Gets or sets the length in bytes.
        /// </summary>
        public uint Length { get; set; }

        /// <summary>
        /// Gets or sets the offset in the package.
        /// </summary>
        public uint Offset { get; set; }

        /// <summary>
        /// Gets or sets which archive this entry is in.
        /// </summary>
        public ushort ArchiveIndex { get; set; }

        /// <summary>
        /// Gets the length in bytes by adding Length and length of SmallData.
        /// </summary>
        public uint TotalLength
        {
            get
            {
                uint totalLength = Length;

                if (SmallData != null)
                {
                    totalLength += (uint)SmallData.Length;
                }

                return totalLength;
            }
        }

        /// <summary>
        /// Gets or sets the preloaded bytes.
        /// </summary>
        public byte[] SmallData { get; set; }

        public string GetFullPath()
        {
            return Path.Combine(DirectoryName, string.Format("{0}.{1}", FileName, TypeName));
        }

        public override string ToString()
        {
            return string.Format("{0} crc=0x{1:x2} metadatasz={2} fnumber={3} ofs=0x{4:x2} sz={5}", GetFullPath(), CRC32, SmallData.Length, ArchiveIndex, Offset, Length);
        }
    }
}
