using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GUI.Types.ParticleRenderer;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

namespace GUI.Utils
{
    internal class FileLoader
    {
        private static readonly Dictionary<string, Package> CachedPackages = new Dictionary<string, Package>();
        private readonly Dictionary<string, Resource> CachedResources = new Dictionary<string, Resource>();

        public Resource LoadFileByAnyMeansNecessary(string file, VrfGuiContext guiContext)
        {
            // TODO: Might conflict where same file name is available in different paths
            if (CachedResources.TryGetValue(file, out var resource) && resource.Reader != null)
            {
                return resource;
            }

            resource = new Resource();

            var entry = guiContext.CurrentPackage.FindEntry(file);

            if (entry != null)
            {
                guiContext.CurrentPackage.ReadEntry(entry, out var output);
                resource.Read(new MemoryStream(output));
                CachedResources[file] = resource;

                return resource;
            }

            var paths = Settings.Config.GameSearchPaths.ToList();
            var packages = new List<Package>();

            foreach (var searchPath in paths.Where(searchPath => searchPath.EndsWith(".vpk")).ToList())
            {
                paths.Remove(searchPath);

                if (!CachedPackages.TryGetValue(searchPath, out var package))
                {
                    Console.WriteLine("Preloading vpk {0}", searchPath);

                    package = new Package();
                    package.Read(searchPath);
                    CachedPackages[searchPath] = package;
                }

                packages.Add(package);
            }

            foreach (var package in packages)
            {
                entry = package?.FindEntry(file);

                if (entry != null)
                {
                    package.ReadEntry(entry, out var output);
                    resource.Read(new MemoryStream(output));
                    CachedResources[file] = resource;

                    return resource;
                }
            }

            var path = FindResourcePath(paths, file, guiContext.FileName);

            if (path == null)
            {
                return null;
            }

            resource.Read(path);
            CachedResources[file] = resource;

            return resource;
        }

        private static string FindResourcePath(IList<string> paths, string file, string currentFullPath = null)
        {
            if (currentFullPath != null)
            {
                paths = paths.OrderByDescending(x => currentFullPath.StartsWith(x, StringComparison.Ordinal)).ToList();
            }

            foreach (var searchPath in paths)
            {
                var path = Path.Combine(searchPath, file);
                path = Path.GetFullPath(path);

                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }
    }
}
