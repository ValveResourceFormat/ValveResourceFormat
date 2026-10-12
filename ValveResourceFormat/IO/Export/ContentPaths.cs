using System.Collections.Frozen;
using System.IO;

namespace ValveResourceFormat.IO;

/// <summary>
/// Maps between content paths, such as <c>models/props/crate.vmdl</c>, and folders on disk.
/// A content path is relative to a mod or addon root, uses forward slashes and never leaves that root.
/// </summary>
public static class ContentPaths
{
    private const string AddonsSuffix = "_addons";

    /// <summary>
    /// Top-level asset folders of game content, used to find where the content path of a file on disk starts when it is
    /// not in an addon or mod.
    /// </summary>
    public static IReadOnlySet<string> TopLevelFolders { get; } = new[]
    {
        "animation",
        "animgraphs",
        "expressions",
        "maps",
        "materials",
        "models",
        "panorama",
        "particles",
        "postprocess",
        "postprocessing",
        "pulse",
        "scenes",
        "smart_props",
        "smartprops",
        "soundevents",
        "sounds",
        "soundstacks",
        "stickers",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the folder that content paths are written under, from a folder picked as the output.
    /// </summary>
    /// <remarks>
    /// The first of these that applies wins:
    /// <list type="number">
    /// <item>The picked folder or its closest ancestor that is an addon or mod root, see <see cref="IsContentRoot"/>.</item>
    /// <item>The ancestor reached by stripping trailing folders of the picked folder that match the start of <paramref name="contentFolder"/>.</item>
    /// <item>The picked folder itself.</item>
    /// </list>
    /// </remarks>
    /// <param name="folder">The picked output folder.</param>
    /// <param name="contentFolder">
    /// The content folder of what is written: the folder of a single file, or the folder all written files are in, see
    /// <see cref="GetCommonDirectory"/>.
    /// </param>
    public static string ResolveOutputRoot(string folder, string? contentFolder = null)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

        if (FindContentRootOfFullPath(root) is { } contentRoot)
        {
            return contentRoot;
        }

        if (contentFolder != null)
        {
            var contentFolders = Normalize(contentFolder).Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (var count = contentFolders.Length; count > 0; count--)
            {
                if (StripTrailingFolders(root, contentFolders.AsSpan(0, count)) is { } strippedRoot)
                {
                    return strippedRoot;
                }
            }
        }

        return root;
    }

    /// <summary>
    /// Whether a folder is the root of an addon (<c>game/&lt;game&gt;_addons/&lt;addon&gt;</c> or <c>content/&lt;game&gt;_addons/&lt;addon&gt;</c>)
    /// or of a mod (<c>game/&lt;mod&gt;</c> with a gameinfo.gi, or the <c>content/&lt;mod&gt;</c> next to one).
    /// </summary>
    public static bool IsContentRoot(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        folder = Path.TrimEndingDirectorySeparator(folder);

        var parent = Path.GetDirectoryName(folder);
        var grandParent = Path.GetDirectoryName(parent);

        if (parent == null || grandParent == null)
        {
            return false;
        }

        var name = Path.GetFileName(folder);
        var parentName = Path.GetFileName(parent);
        var grandParentName = Path.GetFileName(grandParent);

        if (parentName.Length > AddonsSuffix.Length && parentName.EndsWith(AddonsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return IsGameOrContent(grandParentName);
        }

        if (name.EndsWith(AddonsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (parentName.Equals("game", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(Path.Join(folder, "gameinfo.gi"));
        }

        if (parentName.Equals("content", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(Path.Join(grandParent, "game", name, "gameinfo.gi"));
        }

        return false;
    }

    /// <summary>
    /// Whether a path ends with the <c>_c</c> suffix of compiled resources, ignoring case.
    /// </summary>
    public static bool HasCompiledSuffix(ReadOnlySpan<char> path)
        => path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes the <c>_c</c> suffix of compiled resources from a path or extension, ignoring case.
    /// </summary>
    /// <returns>The path without the suffix, or the path itself when it has none.</returns>
    public static string TrimCompiledSuffix(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return HasCompiledSuffix(path) ? path[..^GameFileLoader.CompiledFileSuffix.Length] : path;
    }

    /// <inheritdoc cref="TrimCompiledSuffix(string)"/>
    public static ReadOnlySpan<char> TrimCompiledSuffix(ReadOnlySpan<char> path)
        => HasCompiledSuffix(path) ? path[..^GameFileLoader.CompiledFileSuffix.Length] : path;

    /// <summary>
    /// Finds the closest addon or mod root that is the given folder or one of its ancestors, see <see cref="IsContentRoot"/>.
    /// </summary>
    /// <returns>The full path of the root, or null when the folder is in none.</returns>
    public static string? FindContentRoot(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return FindContentRootOfFullPath(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
    }

    /// <summary>
    /// Gets the content path of a file on disk: its path below the addon or mod root it is in, or else its path from the
    /// first of the <see cref="TopLevelFolders"/> it is in.
    /// </summary>
    /// <returns>The content path, or null when the file is in neither.</returns>
    public static string? GetContentPathFromDisk(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fullPath = Path.GetFullPath(path);

        if (Path.GetDirectoryName(fullPath) is { } directory && FindContentRoot(directory) is { } contentRoot)
        {
            return Normalize(Path.GetRelativePath(contentRoot, fullPath));
        }

        var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (TopLevelFolders.Contains(segments[i]))
            {
                return string.Join('/', segments[i..]);
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the content path a file on disk is written at, see <see cref="GetContentPathFromDisk(string)"/>. A file
    /// outside of any addon, mod or known asset folder gets its bare file name.
    /// </summary>
    /// <param name="path">The file on disk.</param>
    /// <param name="warning">Receives a warning when the file gets its bare file name.</param>
    public static string GetContentPathFromDiskOrFileName(string path, Action<string>? warning)
    {
        if (GetContentPathFromDisk(path) is { } contentPath)
        {
            return contentPath;
        }

        var fileName = Path.GetFileName(path);
        warning?.Invoke($"\"{path}\" is not in an addon, mod or asset folder, its files are placed as if it was at \"{fileName}\".");
        return fileName;
    }

    /// <summary>
    /// Normalizes a content path to forward slashes without empty or <c>.</c> segments.
    /// </summary>
    public static string Normalize(string path) => Normalize(path, out _);

    /// <summary>
    /// Normalizes a content path to forward slashes without empty or <c>.</c> segments, resolving <c>..</c> segments. A
    /// path that would leave the root is brought back under it: an absolute path loses its root, and <c>..</c> segments
    /// that go above the root are dropped. Use <see cref="GetContentPathFromDisk"/> for files on disk.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <param name="escapedRoot">Whether the path was absolute or went above the root.</param>
    public static string Normalize(string path, out bool escapedRoot)
    {
        ArgumentNullException.ThrowIfNull(path);

        path = path.Replace('\\', '/');
        escapedRoot = Path.IsPathRooted(path);

        var segments = new List<string>();

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    escapedRoot = true;
                }

                continue;
            }

            if (segment == "." || (segments.Count == 0 && segment.EndsWith(':')))
            {
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// Gets the folder of a content path, with forward slashes.
    /// </summary>
    /// <returns>The folder, or an empty string when the path has none.</returns>
    public static string GetDirectoryName(string path) => (Path.GetDirectoryName(path) ?? string.Empty).Replace('\\', '/');

    /// <summary>
    /// Gets the deepest content folder that holds every one of the given content paths.
    /// </summary>
    /// <returns>The folder, or an empty string when the paths share none.</returns>
    public static string GetCommonDirectory(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        string[]? commonFolders = null;
        var commonCount = 0;

        foreach (var path in paths)
        {
            var folders = GetDirectoryName(Normalize(path)).Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (commonFolders == null)
            {
                commonFolders = folders;
                commonCount = folders.Length;
                continue;
            }

            commonCount = Math.Min(commonCount, folders.Length);

            for (var i = 0; i < commonCount; i++)
            {
                if (!folders[i].Equals(commonFolders[i], StringComparison.OrdinalIgnoreCase))
                {
                    commonCount = i;
                    break;
                }
            }
        }

        return commonFolders == null ? string.Empty : string.Join('/', commonFolders[..commonCount]);
    }

    /// <summary>
    /// Gets the path of a file or folder relative to a folder, with forward slashes.
    /// </summary>
    public static string GetRelativePath(string relativeTo, string path) => Path.GetRelativePath(relativeTo, path).Replace('\\', '/');

    /// <summary>
    /// Joins a content folder, such as the folder of another content path, and a path below it into a normalized
    /// content path, see <see cref="Normalize(string)"/>.
    /// </summary>
    public static string Join(string? folder, string path) => Normalize(Path.Join(folder, path));

    /// <summary>
    /// Changes the extension of a content path and normalizes it, see <see cref="Normalize(string)"/>.
    /// </summary>
    /// <param name="path">The content path.</param>
    /// <param name="extension">The new extension, with or without its leading dot, or null to remove it.</param>
    public static string ChangeExtension(string path, string? extension)
    {
        ArgumentNullException.ThrowIfNull(path);

        return Normalize(Path.ChangeExtension(path, extension));
    }

    private static string? StripTrailingFolders(string folder, ReadOnlySpan<string> trailingFolders)
    {
        for (var i = trailingFolders.Length - 1; i >= 0; i--)
        {
            if (!Path.GetFileName(folder).Equals(trailingFolders[i], StringComparison.OrdinalIgnoreCase)
                || Path.GetDirectoryName(folder) is not { } parent)
            {
                return null;
            }

            folder = parent;
        }

        return folder;
    }

    private static string? FindContentRootOfFullPath(string folder)
    {
        for (var directory = folder; directory != null; directory = Path.GetDirectoryName(directory))
        {
            if (IsContentRoot(directory))
            {
                return directory;
            }
        }

        return null;
    }

    private static bool IsGameOrContent(string name)
        => name.Equals("game", StringComparison.OrdinalIgnoreCase) || name.Equals("content", StringComparison.OrdinalIgnoreCase);
}
