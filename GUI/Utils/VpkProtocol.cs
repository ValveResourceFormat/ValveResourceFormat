using System.IO;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Utils;

/// <summary>
/// Handles the "vpk:" protocol which references a file inside of a package,
/// for example: "vpk:C:/path/pak01_dir.vpk:inner/file.vmdl_c".
/// </summary>
static class VpkProtocol
{
    public const string UriPrefix = "vpk:";

    private const string PackageSeparator = ".vpk:";

    public sealed class ResolvedFile
    {
        public required string PackagePath { get; init; }
        public required Package Package { get; init; }
        public required PackageEntry Entry { get; init; }
    }

    public static bool IsVpkUri(string path) => path.StartsWith(UriPrefix, StringComparison.InvariantCulture);

    /// <summary>
    /// Formats a vpk: uri for a file or folder inside of a package.
    /// </summary>
    public static string FormatUri(string packagePath, string innerPath)
    {
        var path = packagePath.Replace('\\', '/');

        return innerPath.Length > 0 ? $"{UriPrefix}{path}:{innerPath}" : UriPrefix + path;
    }

    /// <summary>
    /// Parses a vpk: uri, opens the referenced package and finds the inner file in it.
    /// On success the caller takes ownership of <see cref="ResolvedFile.Package"/>.
    /// On failure returns null after logging the error, unless the uri contains no inner file path,
    /// in which case <paramref name="plainFilePath"/> is set and the caller should open that file directly.
    /// </summary>
    public static ResolvedFile? Resolve(string uri, out string? plainFilePath)
    {
        plainFilePath = null;

        var file = System.Net.WebUtility.UrlDecode(uri[UriPrefix.Length..]);

        var innerFilePosition = file.LastIndexOf(PackageSeparator, StringComparison.InvariantCulture);

        if (innerFilePosition == -1)
        {
            Log.Error(nameof(VpkProtocol), $"For vpk: protocol to work, specify a file path inside of the package, for example: \"vpk:C:/path/pak01_dir.vpk:inner/file.vmdl_c\"");

            plainFilePath = file;
            return null;
        }

        var innerFile = file[(innerFilePosition + PackageSeparator.Length)..];
        file = file[..(innerFilePosition + PackageSeparator.Length - 1)];

        if (!File.Exists(file))
        {
            var dirFile = file[..innerFilePosition] + "_dir.vpk";

            if (!File.Exists(dirFile))
            {
                Log.Error(nameof(VpkProtocol), $"File '{file}' does not exist.");
                return null;
            }

            file = dirFile;
        }

        file = Path.GetFullPath(file);
        Log.Info(nameof(VpkProtocol), $"Opening {file}");

        var package = new Package();
        try
        {
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
            package.Read(file);

            var packageFile = package.FindEntry(innerFile) ?? package.FindEntry(innerFile + GameFileLoader.CompiledFileSuffix);

            if (packageFile == null)
            {
                Log.Error(nameof(VpkProtocol), $"File '{innerFile}' does not exist in package '{file}'.");
                return null;
            }

            var resolved = new ResolvedFile
            {
                PackagePath = file,
                Package = package,
                Entry = packageFile,
            };
            package = null;
            return resolved;
        }
        finally
        {
            package?.Dispose();
        }
    }

    /// <summary>
    /// If the given path is a numbered archive (e.g. "pak01_005.vpk") and the corresponding
    /// "_dir.vpk" exists on disk, returns the path to the directory vpk instead.
    /// </summary>
    public static string ResolveDirVpkPath(string fileName)
    {
        if (Regexes.VpkNumberArchive().IsMatch(fileName))
        {
            var fixedPackage = $"{fileName[..^8]}_dir.vpk";

            if (File.Exists(fixedPackage))
            {
                Log.Warn(nameof(VpkProtocol), $"You opened \"{Path.GetFileName(fileName)}\" but there is \"{Path.GetFileName(fixedPackage)}\"");
                return fixedPackage;
            }
        }

        return fileName;
    }
}
