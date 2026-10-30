using System.IO;
using ValveResourceFormat.Source1.FileSystem;

namespace GUI.Utils;

/// <summary>
/// Finds the Source 1 game that a file being viewed belongs to.
/// </summary>
static class Source1Content
{
    private static readonly Dictionary<string, Source1FileSystem?> SharedFileSystems = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The path on disk that the file was opened from: the file itself, or the package it is in.
    /// </summary>
    public static string GetDiskPath(VrfGuiContext vrfGuiContext) => vrfGuiContext.RootContext.FileName;

    /// <summary>
    /// The file system of the game, shared by every file of the game that is open. Its packages stay open for the
    /// whole run, so do not add archives to it.
    /// </summary>
    public static Source1FileSystem? GetShared(VrfGuiContext vrfGuiContext)
    {
        var diskPath = GetDiskPath(vrfGuiContext);
        var key = Source1FileSystem.FindGameInfo(diskPath) ?? Path.GetDirectoryName(diskPath) ?? diskPath;

        lock (SharedFileSystems)
        {
            if (!SharedFileSystems.TryGetValue(key, out var fileSystem))
            {
                fileSystem = Source1FileSystem.FromPath(diskPath);
                SharedFileSystems[key] = fileSystem;
            }

            return fileSystem;
        }
    }

    /// <summary>
    /// A file system of the game for one viewer to own, for content that mounts files of its own such as a map.
    /// </summary>
    public static Source1FileSystem? Create(VrfGuiContext vrfGuiContext)
        => Source1FileSystem.FromPath(GetDiskPath(vrfGuiContext));

    /// <summary>
    /// The stream the viewer was given, rewound, or a new read stream of the file on disk when it was given none.
    /// The caller disposes only a stream it opened, which is the case when <paramref name="stream"/> is null.
    /// </summary>
    public static Stream OpenFile(VrfGuiContext vrfGuiContext, Stream? stream)
    {
        if (stream == null)
        {
            return new FileStream(vrfGuiContext.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// The path of the file in the game, such as <c>materials/brick/wall.vmt</c>.
    /// </summary>
    public static string GetGamePath(VrfGuiContext vrfGuiContext, Source1FileSystem? fileSystem)
    {
        var fileName = vrfGuiContext.FileName;

        if (!Path.IsPathRooted(fileName))
        {
            return fileName.Replace('\\', '/');
        }

        return fileSystem?.GetGamePath(fileName) ?? Path.GetFileName(fileName);
    }
}
