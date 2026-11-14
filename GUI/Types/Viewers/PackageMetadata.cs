using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValvePak;

namespace GUI.Types.Viewers
{
    static class PackageMetadata
    {
        public static TabPage Create(VrfGuiContext guiContext)
        {
            var package = guiContext.CurrentPackage!;
            var entries = package.Entries?.Values.SelectMany(static entries => entries).ToList() ?? [];
            var info = new StringBuilder();

            void AppendSize(string name, long size)
                => info.AppendLine(CultureInfo.InvariantCulture, $"{name}: {size} bytes ({HumanReadableByteSizeFormatter.Format(size)})");

            void AppendChecksum(string name, byte[]? checksum)
            {
                if (checksum != null)
                {
                    info.AppendLine(CultureInfo.InvariantCulture, $"{name}: {Convert.ToHexStringLower(checksum)}");
                }
            }

            var directoryFileSize = package.FilePath != null
                ? new FileInfo(package.FilePath).Length
                : (long)package.HeaderSize + package.TreeSize + package.FileDataSectionSize + package.ArchiveMD5SectionSize + package.OtherMD5SectionSize + package.SignatureSectionSize;

            info.AppendLine(CultureInfo.InvariantCulture, $"Path: {package.FilePath ?? guiContext.DisplayPath}");
            info.AppendLine(CultureInfo.InvariantCulture, $"Version: {package.Version}");
            info.AppendLine(CultureInfo.InvariantCulture, $"Signed: {(package.IsSigned ? $"Yes ({package.SignatureType})" : "No")}");
            AppendSize("File size", directoryFileSize);
            AppendSize("Header size", package.HeaderSize);
            AppendSize("Directory tree size", package.TreeSize);
            AppendSize("File data section size", package.FileDataSectionSize);
            AppendSize("Archive MD5 section size", package.ArchiveMD5SectionSize);
            AppendSize("Other MD5 section size", package.OtherMD5SectionSize);
            AppendSize("Signature section size", package.SignatureSectionSize);

            if (package is { PublicKey: { } publicKey, Signature: { } signature })
            {
                AppendSize("Public key size", publicKey.Length);
                AppendSize("Signature size", signature.Length);
            }

            info.AppendLine();

            info.AppendLine(CultureInfo.InvariantCulture, $"Files: {entries.Count}");
            info.AppendLine(CultureInfo.InvariantCulture, $"File types: {package.Entries?.Count ?? 0}");
            info.AppendLine(CultureInfo.InvariantCulture, $"Folders: {entries.Select(static entry => entry.DirectoryName).Distinct().Count()}");
            info.AppendLine(CultureInfo.InvariantCulture, $"Files in the directory file: {entries.Count(static entry => entry.ArchiveIndex == Package.DirectoryArchiveIndex)}");
            AppendSize("Total size of files", entries.Sum(static entry => (long)entry.TotalLength));
            AppendSize("Preloaded bytes", entries.Sum(static entry => (long)entry.SmallData.Length));
            info.AppendLine();

            AppendChecksum("Tree MD5", package.TreeChecksum);
            AppendChecksum("Archive MD5 section MD5", package.ArchiveMD5EntriesChecksum);
            AppendChecksum("Whole file MD5", package.WholeFileChecksum);
            info.AppendLine(CultureInfo.InvariantCulture, $"Chunk hash fractions: {package.AccessPackFileHashes.Count}");

            var chunks = entries
                .Where(static entry => entry.ArchiveIndex != Package.DirectoryArchiveIndex)
                .GroupBy(static entry => entry.ArchiveIndex)
                .OrderBy(static chunk => chunk.Key)
                .Select(chunk => (Files: chunk.Count(), File: new FileInfo(package.GetChunkFilePath(chunk.Key))))
                .ToList();

            if (chunks.Count > 0)
            {
                var existingChunks = chunks.Where(static chunk => chunk.File.Exists).ToList();
                var chunkFilesSize = existingChunks.Sum(static chunk => chunk.File.Length);

                info.AppendLine();
                info.AppendLine(CultureInfo.InvariantCulture, $"Chunk files: {chunks.Count}");

                if (existingChunks.Count < chunks.Count)
                {
                    info.AppendLine(CultureInfo.InvariantCulture, $"Missing chunk files: {chunks.Count - existingChunks.Count}");
                }

                info.AppendLine(CultureInfo.InvariantCulture, $"Files per chunk: {(double)chunks.Sum(static chunk => chunk.Files) / chunks.Count:0.##}");

                if (existingChunks.Count > 0)
                {
                    var smallest = existingChunks.MinBy(static chunk => chunk.File.Length);
                    var largest = existingChunks.MaxBy(static chunk => chunk.File.Length);

                    AppendSize("Total size of chunk files", chunkFilesSize);
                    AppendSize("Average chunk size", chunkFilesSize / existingChunks.Count);
                    AppendSize(string.Create(CultureInfo.InvariantCulture, $"Smallest chunk ({smallest.File.Name}, {smallest.Files} files)"), smallest.File.Length);
                    AppendSize(string.Create(CultureInfo.InvariantCulture, $"Largest chunk ({largest.File.Name}, {largest.Files} files)"), largest.File.Length);
                    AppendSize("Total package size", directoryFileSize + chunkFilesSize);
                }
            }

            var tab = new ThemedTabPage($"{Path.GetFileName(guiContext.FileName)} metadata")
            {
                ToolTipText = guiContext.DisplayPath,
            };

            tab.Controls.Add(new CodeTextBox(info.ToString())
            {
                Dock = DockStyle.Fill,
            });

            return tab;
        }
    }
}
