using System.IO;
using ValvePak;
using ValveResourceFormat.IO;

namespace CLI
{
    public partial class Decompiler
    {
        private string? VpkCreatePath;
        private int VpkCreateChunkSize; // In MiB, 0 writes everything into a single file

        private int CreateVpk()
        {
            var outputPath = Path.GetFullPath(VpkCreatePath!);
            var multiChunk = VpkCreateChunkSize > 0;
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var lowercasedPaths = 0;
            var lengths = new List<(string EntryPath, long Length)>();

            foreach (var (path, slashedPath) in PackageFileNames.GetFilesToPack(InputFile, outputPath))
            {
                if (IsExcludedVpkFilePath(FixPathSlashes(slashedPath)) || !MatchesExtensionFilter(Path.GetExtension(slashedPath).TrimStart('.')))
                {
                    continue;
                }

                if (!Package.TryParseEntryPath(slashedPath, out var entryPath))
                {
                    Console.Error.WriteLine($"\"{path}\" can not be packed, \"{slashedPath}\" is not a valid path in a VPK.");
                    return 1;
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

                var length = new FileInfo(path).Length;

                if (multiChunk && length > Package.MaxChunkFileSize)
                {
                    Console.Error.WriteLine($"\"{path}\" is larger than 4 GiB, which does not fit into a chunk file.");
                    return 1;
                }

                lengths.Add((entryPath, length));
            }

            if (files.Count == 0)
            {
                Console.Error.WriteLine(HasPathFilter || ExtFilterList != null
                    ? $"No files in \"{InputFile}\" matched the given filters."
                    : $"Unable to find any files in \"{InputFile}\" folder.");
                return 1;
            }

            if (!multiChunk && !PackageFileNames.FitsIntoSingleFile(lengths))
            {
                Console.Error.WriteLine("The files add up to more than 2 GiB, which does not fit into a single VPK. Use --vpk_create_chunk_size to split it into chunk files.");
                return 1;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var package = new Package();
            package.SetFileName(outputPath);

            if (multiChunk)
            {
                package.WriteChunkSize = VpkCreateChunkSize * 1024 * 1024;
            }

            try
            {
                foreach (var (entryPath, path) in files)
                {
                    if (!Quiet)
                    {
                        Console.WriteLine(entryPath);
                    }

                    using var stream = File.OpenRead(path);
                    package.AddFile(entryPath, stream, multiChunk);
                }

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
