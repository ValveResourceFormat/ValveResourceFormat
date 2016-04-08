using System;

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
        /// '/' is always used as a dictionary separator in Valve's implementation.
        /// Directory names are also always lower cased in Valve's implementation.
        /// </summary>
        public string DirectoryName { get; set; }

        /// <summary>
        /// Gets or sets the file extension.
        /// If the file has no extension, this is an empty string.
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
                var totalLength = Length;

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

        /// <summary>
        /// Returns the file name and extension.
        /// </summary>
        /// <returns>File name and extension.</returns>
        public string GetFileName()
        {
            var fileName = FileName;

            if (TypeName != string.Empty)
            {
                fileName += "." + TypeName;
            }

            return fileName;
        }

        /// <summary>
        /// Returns the absolute path of the file in the package.
        /// </summary>
        /// <returns>Absolute path.</returns>
        public string GetFullPath()
        {
            if (DirectoryName == null)
            {
                return GetFileName();
            }

            return DirectoryName + Package.DirectorySeparatorChar + GetFileName();
        }

        public override string ToString()
        {
            return $"{GetFullPath()} crc=0x{CRC32:x2} metadatasz={SmallData.Length} fnumber={ArchiveIndex} ofs=0x{Offset:x2} sz={Length}";
        }
    }
}
