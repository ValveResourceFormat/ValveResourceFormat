using System;
using System.IO;

namespace ValveResourceFormat
{
    public class PackageEntry
    {
        /// <summary>
        /// File name of this entry.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// The name of the directory this file is in.
        /// </summary>
        public string DirectoryName { get; set; }

        /// <summary>
        /// File extension.
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// CRC32 checksum of this entry.
        /// </summary>
        public uint CRC32 { get; set; }

        /// <summary>
        /// Length in bytes.
        /// </summary>
        public uint Length { get; set; }

        /// <summary>
        /// Offset in the package.
        /// </summary>
        public uint Offset { get; set; }

        /// <summary>
        /// Which archive this entry is in.
        /// </summary>
        public ushort ArchiveIndex { get; set; }

        /// <summary>
        /// Returns the length in bytes by adding Length + SmallData.Length
        /// </summary>
        public uint TotalLength
        {
            get
            {
                uint totalLength = Length;

                if(SmallData != null)
                {
                    totalLength += (uint)SmallData.Length;
                }

                return totalLength;
            }
        }

        /// <summary>
        /// TODO
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
