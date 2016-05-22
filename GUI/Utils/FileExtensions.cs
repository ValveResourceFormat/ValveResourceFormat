using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ValveResourceFormat;

namespace GUI.Utils
{
    internal static class FileExtensions
    {
        private static Dictionary<string, Package> CachedPackages = new Dictionary<string, Package>();

        // http://stackoverflow.com/a/4975942/272647
        public static string ToFileSizeString(this uint byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            string result;

            if (byteCount == 0)
            {
                result = string.Format("{0} {1}", byteCount, suf[0]);
            }
            else
            {
                var absoluteByteCount = Math.Abs(byteCount);
                var place = Convert.ToInt32(Math.Floor(Math.Log(absoluteByteCount, 1024)));
                var num = Math.Round(absoluteByteCount / Math.Pow(1024, place), 1);
                result = string.Format("{0} {1}", Math.Sign(byteCount) * num, suf[place]);
            }

            return result;
        }

        public static bool LoadFileByAnyMeansNecessary(Resource resource, string file, string currentFullPath, Package currentPackage)
        {
            var entry = currentPackage?.FindEntry(file);

            if (entry != null)
            {
                byte[] output;
                currentPackage.ReadEntry(entry, out output);
                resource.Read(new MemoryStream(output));

                return true;
            }

            var paths = Settings.GameSearchPaths.ToList();
            var packages = new List<Package>();

            foreach (var searchPath in paths.Where(searchPath => searchPath.EndsWith(".vpk")).ToList())
            {
                paths.Remove(searchPath);

                Package package;
                if (!CachedPackages.TryGetValue(searchPath, out package))
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
                    byte[] output;
                    package.ReadEntry(entry, out output);
                    resource.Read(new MemoryStream(output));

                    return true;
                }
            }

            var path = FindResourcePath(paths, file, currentFullPath);

            if (path == null)
            {
                return false;
            }

            resource.Read(path);

            return true;
        }

        public static string FindResourcePath(string file, string currentFullPath = null)
        {
            return FindResourcePath(Settings.GameSearchPaths.Where(path => !path.EndsWith(".vpk")).ToList(), file, currentFullPath);
        }

        public static string FindResourcePath(IList<string> paths, string file, string currentFullPath = null)
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
