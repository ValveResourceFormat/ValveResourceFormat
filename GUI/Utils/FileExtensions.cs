using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
                result = String.Format("{0} {1}", byteCount, suf[0]);
            }
            else
            {
                long absoluteByteCount = Math.Abs(byteCount);
                int place = Convert.ToInt32(Math.Floor(Math.Log(absoluteByteCount, 1024)));
                double num = Math.Round(absoluteByteCount / Math.Pow(1024, place), 1);
                result = String.Format("{0} {1}", (Math.Sign(byteCount) * num), suf[place]);
            }

            return result;
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
                var path = Path.Combine(searchPath, file + "_c");
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
