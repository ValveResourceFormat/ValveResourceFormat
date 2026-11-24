using System.Diagnostics.CodeAnalysis;

namespace ValveResourceFormat.IO;

/// <summary>
/// Maps the content paths of the files an export writes to the content paths they are written at, so references to
/// them can be rewritten.
/// </summary>
public sealed class ContentPathMap
{
    private readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets whether the map has no entries.</summary>
    public bool IsEmpty => Paths.Count == 0;

    /// <summary>
    /// Adds a file. Both paths are normalized and lose a trailing <c>_c</c>.
    /// </summary>
    /// <param name="from">The original content path.</param>
    /// <param name="to">The content path it is written at.</param>
    public void Add(string from, string to)
    {
        Paths[GetKey(from)] = GetKey(to);
    }

    /// <summary>
    /// Adds a folder.
    /// </summary>
    /// <param name="fromFolder">The original content folder.</param>
    /// <param name="toFolder">The content folder its files are written in.</param>
    public void AddFolder(string fromFolder, string toFolder)
    {
        Add(fromFolder, toFolder);
    }

    /// <summary>
    /// Maps a content path that is in the map, keeping the <c>_c</c> suffix of the value.
    /// </summary>
    /// <param name="value">The content path, with forward or back slashes.</param>
    /// <param name="mapped">The content path it is written at.</param>
    /// <returns>Whether the path is in the map.</returns>
    public bool TryMap(string value, [NotNullWhen(true)] out string? mapped)
    {
        if (!Paths.TryGetValue(GetKey(value), out var to))
        {
            mapped = null;
            return false;
        }

        mapped = ContentPaths.HasCompiledSuffix(value) ? to + GameFileLoader.CompiledFileSuffix : to;
        return true;
    }

    /// <summary>
    /// Maps a content path, or returns it as it is when it is not in the map.
    /// </summary>
    public string Map(string value) => TryMap(value, out var mapped) ? mapped : value;

    private static string GetKey(string path) => ContentPaths.TrimCompiledSuffix(ContentPaths.Normalize(path));
}
