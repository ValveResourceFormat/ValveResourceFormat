using System.Globalization;
using System.IO;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// File names of VPK packages: a package split into chunks is a <c>name_dir.vpk</c> directory file with numbered
    /// <c>name_000.vpk</c> archive chunks next to it, and a game folder mounts its numbered <c>pakNN_dir.vpk</c> packages.
    /// </summary>
    public static class PackageFileNames
    {
        private const string Extension = ".vpk";
        private const string DirectorySuffix = "_dir";

        /// <summary>
        /// Whether a path names an archive chunk of a split package, such as <c>pak01_003.vpk</c>.
        /// </summary>
        public static bool IsArchiveChunk(ReadOnlySpan<char> path)
        {
            if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var name = path[..^Extension.Length];

            return name.Length > 4 && name[^4] == '_' && char.IsAsciiDigit(name[^3]) && char.IsAsciiDigit(name[^2]) && char.IsAsciiDigit(name[^1]);
        }

        /// <summary>
        /// Gets the name of a package without the <c>.vpk</c> extension and the <c>_dir</c> or archive chunk suffix, such as
        /// <c>pak01</c> for <c>pak01_dir.vpk</c> and <c>pak01_003.vpk</c>. A package read from a <c>_dir.vpk</c> file has
        /// this as its file name.
        /// </summary>
        public static string GetPackageName(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            if (IsArchiveChunk(path))
            {
                return path[..^(Extension.Length + 4)];
            }

            if (path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^Extension.Length];
            }

            if (path.EndsWith(DirectorySuffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^DirectorySuffix.Length];
            }

            return path;
        }

        /// <summary>
        /// Gets the <c>_dir.vpk</c> directory file of a package, such as <c>pak01_dir.vpk</c> for <c>pak01_003.vpk</c> or
        /// <c>pak01.vpk</c>.
        /// </summary>
        public static string GetDirectoryFile(string path) => string.Concat(GetPackageName(path), DirectorySuffix, Extension);

        /// <summary>
        /// Gets the file name of an archive chunk of a split package, such as <c>pak01_003.vpk</c>.
        /// </summary>
        /// <param name="packageName">The package name, see <see cref="GetPackageName"/>.</param>
        /// <param name="archiveIndex">The index of the chunk.</param>
        public static string GetArchiveChunkFile(string packageName, int archiveIndex)
            => string.Create(CultureInfo.InvariantCulture, $"{packageName}_{archiveIndex:D3}{Extension}");

        /// <summary>
        /// Gets the number of a <c>pakNN_dir.vpk</c> file name, or -1 for any other name.
        /// </summary>
        public static int GetPakNumber(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);

            if (fileName.Length != "pak00_dir.vpk".Length
                || !fileName.StartsWith("pak", StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith(DirectorySuffix + Extension, StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(fileName.AsSpan("pak".Length, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return -1;
            }

            return number;
        }

        /// <summary>
        /// Finds the <c>pakNN_dir.vpk</c> files a game mounts from a folder, in numeric order. Numbers may have gaps, and
        /// <c>pak00_dir.vpk</c> is never mounted.
        /// </summary>
        public static IEnumerable<string> EnumeratePakDirectoryFiles(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            var paks = new SortedList<int, string>();

            foreach (var vpk in Directory.EnumerateFiles(folder, "pak*_dir.vpk"))
            {
                var number = GetPakNumber(Path.GetFileName(vpk));

                if (number > 0)
                {
                    paks.Add(number, vpk);
                }
            }

            return paks.Values;
        }

        /// <summary>
        /// Finds an addon packed as <c>folder/name.vpk</c>, or split into chunks with <c>folder/name_dir.vpk</c>.
        /// </summary>
        /// <returns>The path of the package, or <see langword="null"/> when there is none.</returns>
        public static string? FindAddonPackage(string folder, string name)
        {
            var vpk = Path.Join(folder, string.Concat(name, Extension));

            if (File.Exists(vpk))
            {
                return vpk;
            }

            vpk = Path.Join(folder, string.Concat(name, DirectorySuffix, Extension));

            return File.Exists(vpk) ? vpk : null;
        }
    }
}
