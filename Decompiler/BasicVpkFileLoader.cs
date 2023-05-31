using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using SteamDatabase.ValvePak;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.IO
{
    public class BasicVpkFileLoader : IFileLoader
    {
        private readonly Package CurrentPackage;

        public BasicVpkFileLoader(Package package)
        {
            CurrentPackage = package ?? throw new ArgumentNullException(nameof(package));
        }

        public Resource LoadFile(string file)
        {
            var entry = CurrentPackage.FindEntry(file);

            if (entry == null)
            {
                return null;
            }

            CurrentPackage.ReadEntry(entry, out var output, false);

            var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(new MemoryStream(output));

            return resource;
        }

        public ShaderCollection LoadShader(string shaderName) => null;

        public static Stream GetPackageEntryStream(Package package, PackageEntry entry)
        {
            // Files in a vpk that isn't split
            if (!package.IsDirVPK || entry.ArchiveIndex == 32767 || entry.SmallData.Length > 0)
            {
                byte[] output;

                lock (package)
                {
                    package.ReadEntry(entry, out output, false);
                }

                return new MemoryStream(output);
            }

            var path = $"{package.FileName}_{entry.ArchiveIndex:D3}.vpk";
            var stream = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            return stream.CreateViewStream(entry.Offset, entry.Length, MemoryMappedFileAccess.Read);
        }
    }
}
