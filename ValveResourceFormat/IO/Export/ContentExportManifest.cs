using System.Globalization;
using System.IO;
using ValvePak;

namespace ValveResourceFormat.IO;

/// <summary>
/// The files of a package that an export wrote, with their CRC32, see <see cref="ContentExporter.ManifestOptions"/>.
/// </summary>
internal sealed class ContentExportManifest
{
    private readonly Package Package;
    private readonly string FilePath;
    private readonly string Header;
    private readonly Dictionary<string, uint> Entries = new(StringComparer.OrdinalIgnoreCase);

    private ContentExportManifest(Package package, string filePath, string header)
    {
        Package = package;
        FilePath = filePath;
        Header = header;
    }

    /// <summary>
    /// Loads the manifest of a package from the output root, or starts an empty one when there is none or it was written
    /// by another version, for another package or with other options.
    /// </summary>
    public static ContentExportManifest Load(ContentExporter exporter, Package package)
    {
        var packagePath = Path.GetFullPath(string.Concat(package.FileName, package.IsDirVPK ? "_dir" : string.Empty, ".vpk"));
        var version = typeof(ContentExporter).Assembly.GetName().Version;
        var manifest = new ContentExportManifest(
            package,
            Path.Join(exporter.OutputRoot, string.Concat(Path.GetFileName(packagePath), ".manifest.txt")),
            string.Create(CultureInfo.InvariantCulture, $"// s2v_version={version} package={packagePath} dependencies={exporter.IncludeDependencies} {exporter.ExtractOptions} {exporter.ManifestOptions}"));

        if (!File.Exists(manifest.FilePath))
        {
            return manifest;
        }

        using var reader = new StreamReader(manifest.FilePath);

        if (reader.ReadLine() != manifest.Header)
        {
            exporter.Warning?.Invoke($"\"{manifest.FilePath}\" was written by another version, for another package or with other options, it is ignored.");
            return manifest;
        }

        while (reader.ReadLine() is { } line)
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);

            if (space > 0 && uint.TryParse(line.AsSpan(0, space), NumberStyles.None, CultureInfo.InvariantCulture, out var crc32))
            {
                manifest.Entries[line[(space + 1)..]] = crc32;
            }
        }

        return manifest;
    }

    /// <summary>
    /// Whether a file was recorded with the CRC32 it has in the package now.
    /// </summary>
    public bool IsUnchanged(ContentExportItem item)
        => Package.FindEntry(item.ContentPath) is { } entry && Entries.TryGetValue(item.ContentPath, out var recorded) && recorded == entry.CRC32;

    /// <summary>
    /// Records a file that exported, and drops one that did not so the next export retries it.
    /// </summary>
    public void Record(ContentExportItem item, ContentExportResult result)
    {
        switch (result)
        {
            case ContentExportResult.Unchanged:
                break;

            case ContentExportResult.Exported or ContentExportResult.Handled when Package.FindEntry(item.ContentPath) is { } entry:
                Entries[item.ContentPath] = entry.CRC32;
                break;

            default:
                Entries.Remove(item.ContentPath);
                break;
        }
    }

    /// <summary>
    /// Writes the manifest, without the files that are no longer in the package.
    /// </summary>
    public void Save(IProgress<string>? progress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        using var writer = new StreamWriter(FilePath);
        writer.WriteLine(Header);

        foreach (var (contentPath, crc32) in Entries)
        {
            if (Package.FindEntry(contentPath) == null)
            {
                progress?.Report($"\"{contentPath}\" is no longer in the package, it is dropped from the manifest");
                continue;
            }

            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{crc32} {contentPath}"));
        }
    }
}
