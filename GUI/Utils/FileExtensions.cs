using System;
using System.IO;
using System.Linq;
using ValveResourceFormat;

namespace GUI.Utils
{
    public static class FileExtensions
    {
        /// <summary>
        /// http://stackoverflow.com/a/4975942/272647
        /// </summary>
        /// <returns></returns>
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
                result = string.Format("{0} {1}", (Math.Sign(byteCount) * num), suf[place]);
            }

            return result;
        }

        public static bool LoadFileByAnyMeansNecessary(Resource resource, string file, string currentFullPath, Package currentPackage)
        {
            if (currentPackage != null)
            {
                var entry = FindPackageEntry(currentPackage, file);

                if (entry != null)
                {
                    byte[] output;
                    currentPackage.ReadEntry(entry, out output);
                    resource.Read(new MemoryStream(output));

                    return true;
                }
            }

            var path = FindResourcePath(file, currentFullPath);

            if (path == null)
            {
                return false;
            }

            resource.Read(path);

            return true;
        }

        public static PackageEntry FindPackageEntry(Package currentPackage, string file)
        {
            var extension = Path.GetExtension(file).Substring(1);

            if (!currentPackage.Entries.ContainsKey(extension))
            {
                return null;
            }

            file = file.Replace("/", "\\");

            return currentPackage.Entries[extension].FirstOrDefault(x => x.GetFullPath() == file);
        }

        public static string FindResourcePath(string file, string currentFullPath = null)
        {
            var paths = Settings.GameSearchPaths;

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
