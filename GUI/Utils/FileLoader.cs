//#define DEBUG_FILE_LOAD

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

namespace GUI.Utils
{
    internal class FileLoader
    {
        private static readonly Dictionary<string, Package> CachedPackages = new Dictionary<string, Package>();
        private readonly Dictionary<string, Resource> CachedResources = new Dictionary<string, Resource>();

        public void ClearCache()
        {
            foreach (var resource in CachedResources.Values)
            {
                resource.Dispose();
            }

            CachedResources.Clear();
        }

        public Resource LoadFileByAnyMeansNecessary(string file, VrfGuiContext guiContext)
        {
            // TODO: Might conflict where same file name is available in different paths
            if (CachedResources.TryGetValue(file, out var resource) && resource.Reader != null)
            {
                return resource;
            }

            resource = new Resource();

            var entry = guiContext.CurrentPackage?.FindEntry(file);

            if (entry != null)
            {
#if DEBUG_FILE_LOAD
                Console.WriteLine($"Loaded \"{file}\" from current vpk");
#endif

                guiContext.CurrentPackage.ReadEntry(entry, out var output, false);
                resource.Read(new MemoryStream(output));
                CachedResources[file] = resource;

                return resource;
            }

            entry = guiContext.ParentPackage?.FindEntry(file);

            if (entry != null)
            {
#if DEBUG_FILE_LOAD
                Console.WriteLine($"Loaded \"{file}\" from parent vpk");
#endif

                guiContext.ParentPackage.ReadEntry(entry, out var output, false);
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
                    Console.WriteLine($"Preloading vpk \"{searchPath}\"");

                    package = new Package();
                    package.Read(searchPath);
                    CachedPackages[searchPath] = package;
                }

                packages.Add(package);
            }

            if (guiContext.ParentPackage != null && guiContext.ParentPackage.Entries.ContainsKey("vpk"))
            {
                foreach (var searchPath in guiContext.ParentPackage.Entries["vpk"])
                {
                    if (!CachedPackages.TryGetValue(searchPath.GetFileName(), out var package))
                    {
                        Console.WriteLine($"Preloading vpk from parent vpk \"{searchPath}\"");

                        guiContext.ParentPackage.ReadEntry(searchPath, out var vpk, false);
                        var ms = new MemoryStream(vpk);
                        package = new Package();
                        package.SetFileName(searchPath.GetFileName());
                        package.Read(ms);
                        CachedPackages[searchPath.GetFileName()] = package;
                    }

                    packages.Add(package);
                }
            }

            foreach (var package in packages)
            {
                entry = package?.FindEntry(file);

                if (entry != null)
                {
#if DEBUG_FILE_LOAD
                    Console.WriteLine($"Loaded \"{file}\" from preloaded vpk \"{package.FileName}\"");
#endif

                    package.ReadEntry(entry, out var output, false);
                    resource.Read(new MemoryStream(output));
                    CachedResources[file] = resource;

                    return resource;
                }
            }

            var path = FindResourcePath(paths, file, guiContext.FileName);

            if (path == null)
            {
                Console.Error.WriteLine($"Failed to load \"{file}\". Did you configure VPK paths in settings correctly?");

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
#if DEBUG_FILE_LOAD
                    Console.WriteLine($"Loaded \"{file}\" from disk: \"{path}\"");
#endif

                    return path;
                }
            }

            return null;
        }
    }
}
