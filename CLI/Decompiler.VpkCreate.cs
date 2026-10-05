using System.IO;
using System.IO.Enumeration;
using ValvePak;

namespace CLI
{
    public partial class Decompiler
    {
        private string? VpkCreatePath;
        private int VpkCreateChunkSize; // In megabytes, 0 writes everything into a single file

        private int CreateVpk()
        {
            var outputPath = Path.GetFullPath(VpkCreatePath!);
            var multiChunk = VpkCreateChunkSize > 0;

            // "_dir.vpk" and "_000.vpk" have the same length
            var chunkFileBaseName = outputPath.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase) ? outputPath[..^8] : null;

            // Skip dot files and folders such as .git, which are not game files
            var enumeration = new FileSystemEnumerable<(string Path, long Length)>(
                InputFile,
                (ref entry) => (entry.ToFullPath(), entry.Length),
                new EnumerationOptions { RecurseSubdirectories = true })
            {
                ShouldIncludePredicate = (ref entry) => !entry.IsDirectory && !entry.FileName.StartsWith('.'),
                ShouldRecursePredicate = (ref entry) => !entry.FileName.StartsWith('.'),
            };

            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var lowercasedPaths = 0;
            long totalSize = 0;

            foreach (var (path, length) in enumeration)
            {
                // The output may be written into the input folder, so it must not pack itself or its old chunk files
                if (path.Equals(outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (chunkFileBaseName != null && VpkArchiveIndexRegex().IsMatch(path) && path.AsSpan()[..^8].Equals(chunkFileBaseName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(InputFile, path);

                if (IsExcludedVpkFilePath(relativePath))
                {
                    continue;
                }

                // Paths in packages always use forward slashes and are lowercase
                var slashedPath = relativePath.Replace('\\', '/');
                var entryPath = slashedPath.ToLowerInvariant();

                if (!MatchesExtensionFilter(Path.GetExtension(entryPath).TrimStart('.')))
                {
                    continue;
                }

                if (!files.TryAdd(entryPath, path))
                {
                    Console.Error.WriteLine($"\"{files[entryPath]}\" and \"{path}\" would both be packed as \"{entryPath}\", paths in VPKs are case-insensitive.");
                    return 1;
                }

                if (entryPath != slashedPath)
                {
                    lowercasedPaths++;
                }

                if (length > int.MaxValue)
                {
                    Console.Error.WriteLine($"\"{path}\" is larger than 2 GiB, which is not supported.");
                    return 1;
                }

                totalSize += length;
            }

            if (files.Count == 0)
            {
                Console.Error.WriteLine(HasPathFilter || ExtFilterList != null
                    ? $"No files in \"{InputFile}\" matched the given filters."
                    : $"Unable to find any files in \"{InputFile}\" folder.");
                return 1;
            }

            if (!multiChunk && totalSize > int.MaxValue)
            {
                Console.Error.WriteLine("The files add up to more than 2 GiB, which does not fit into a single VPK. Use --vpk_create_chunk_size to split it into chunk files.");
                return 1;
            }

            using var package = new Package();

            if (multiChunk)
            {
                package.WriteChunkSize = VpkCreateChunkSize * 1024 * 1024;
            }

            foreach (var (entryPath, path) in files)
            {
                if (!Quiet)
                {
                    Console.WriteLine(entryPath);
                }

                package.AddFile(entryPath, File.ReadAllBytes(path), multiChunk);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            try
            {
                package.Write(outputPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Console.Error.WriteLine($"Failed to write \"{outputPath}\": {e.Message}");
                return 1;
            }

            if (lowercasedPaths > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Error.WriteLine($"Warning: Lowercased {lowercasedPaths} file path(s), paths in VPKs are lowercase.");
                Console.ResetColor();
            }

            Console.WriteLine($"--- Packed {files.Count} files into \"{outputPath}\"");

            return 0;
        }
    }
}
