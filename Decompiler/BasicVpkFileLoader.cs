using System;
using System.IO;
using SteamDatabase.ValvePak;

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
    }
}
