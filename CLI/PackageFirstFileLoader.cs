using System.IO;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;

namespace CLI
{
    /// <summary>
    /// Finds files in a package first, then falls back to another loader, so that packages can share one loader for the game files.
    /// </summary>
    internal sealed class PackageFirstFileLoader(Package package, IFileLoader fallback) : IFileLoader
    {
        public Resource? LoadFile(string file)
        {
            var entry = package.FindEntry(file);

            if (entry == null)
            {
                return fallback.LoadFile(file);
            }

            var resource = new Resource
            {
                FileName = file,
            };

            try
            {
                resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));
                return resource;
            }
            catch
            {
                resource.Dispose();
                throw;
            }
        }

        public Resource? LoadFileCompiled(string file) => LoadFile(string.Concat(file, GameFileLoader.CompiledFileSuffix));

        public ShaderCollection? LoadShader(string shaderName) => fallback.LoadShader(shaderName);

        public Stream? GetFileStream(string file)
        {
            var entry = package.FindEntry(file);

            return entry != null ? GameFileLoader.GetPackageEntryStream(package, entry) : fallback.GetFileStream(file);
        }
    }
}
