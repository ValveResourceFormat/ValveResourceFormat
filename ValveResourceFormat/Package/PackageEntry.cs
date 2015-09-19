using System;

namespace ValveResourceFormat
{
    public class PackageEntry
    {
        public string FileName;
        public string DirectoryName;
        public string TypeName;
        public uint CRC32;
        public uint Length;
        public uint Offset;
        public ushort ArchiveIndex;
        public byte[] SmallData;
    }
}
