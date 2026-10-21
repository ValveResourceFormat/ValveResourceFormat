using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using ValvePak;

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
        /// Whether a path is an archive chunk of a split package, such as <c>pak01_003.vpk</c> next to an existing
        /// <c>pak01_dir.vpk</c>. Without that directory file, a name such as <c>surf_2024.vpk</c> is a package of its own.
        /// </summary>
        public static bool IsArchiveChunk(string path) => TryGetArchiveChunkDirectoryFile(path, out _);

        /// <summary>
        /// Gets the name of a package without the <c>.vpk</c> extension and the <c>_dir</c> or archive chunk suffix, such as
        /// <c>pak01</c> for <c>pak01_dir.vpk</c> and <c>pak01_003.vpk</c>. A package read from a <c>_dir.vpk</c> file has
        /// this as its file name. The chunk suffix is only removed from an archive chunk, see <see cref="IsArchiveChunk"/>.
        /// </summary>
        public static string GetPackageName(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            if (TryGetArchiveChunkDirectoryFile(path, out var directoryFile))
            {
                return directoryFile[..^(DirectorySuffix.Length + Extension.Length)];
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
        /// <c>pak01.vpk</c>. The chunk suffix is only removed from an archive chunk, see <see cref="IsArchiveChunk"/>.
        /// </summary>
        public static string GetDirectoryFile(string path) => string.Concat(GetPackageName(path), DirectorySuffix, Extension);

        /// <summary>
        /// Gets the <c>_dir.vpk</c> directory file a path would be an archive chunk of, such as <c>pak01_dir.vpk</c> for
        /// <c>pak01_003.vpk</c>, from the name alone. Unlike <see cref="TryGetArchiveChunkDirectoryFile"/>, it does not check that the directory file exists.
        /// </summary>
        /// <returns>False if the path does not have the form of an archive chunk.</returns>
        public static bool TryGetChunkDirectoryFileName(string path, [NotNullWhen(true)] out string? directoryFile)
        {
            directoryFile = Package.TryParseChunkFilePath(path, out var baseName, out _) ? string.Concat(baseName, DirectorySuffix, Extension) : null;

            return directoryFile != null;
        }

        /// <summary>
        /// Gets the file name of an archive chunk of a split package, such as <c>pak01_003.vpk</c>.
        /// </summary>
        /// <param name="packageName">The package name, see <see cref="GetPackageName"/>.</param>
        /// <param name="archiveIndex">The index of the chunk.</param>
        public static string GetArchiveChunkFile(string packageName, int archiveIndex)
            => string.Create(CultureInfo.InvariantCulture, $"{packageName}_{archiveIndex:D3}{Extension}");

        /// <summary>
        /// Gets the existing <c>_dir.vpk</c> directory file of an archive chunk, such as <c>pak01_dir.vpk</c> for
        /// <c>pak01_003.vpk</c>, see <see cref="IsArchiveChunk"/>.
        /// </summary>
        /// <returns>False if the path is not an archive chunk.</returns>
        public static bool TryGetArchiveChunkDirectoryFile(string path, [NotNullWhen(true)] out string? directoryFile)
        {
            directoryFile = TryGetChunkDirectoryFileName(path, out var chunkDirectoryFile) && File.Exists(chunkDirectoryFile) ? chunkDirectoryFile : null;

            return directoryFile != null;
        }

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
