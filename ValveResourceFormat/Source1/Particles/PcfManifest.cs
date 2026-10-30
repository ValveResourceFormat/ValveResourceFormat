using System.IO;
using System.Text.RegularExpressions;

namespace ValveResourceFormat.Source1.Particles;

/// <summary>
/// A text file that lists the <c>.pcf</c> files of a game or of a map, each as a <c>file</c> key.
/// </summary>
internal static partial class PcfManifest
{
    /// <summary>The manifest of a game.</summary>
    public const string GamePath = "particles/particles_manifest.txt";

    /// <summary>The manifest a map keeps in its pakfile, next to the files it lists.</summary>
    public const string PakPath = "particles.txt";

    /// <summary>Gets the path of the manifest that is shipped with a map.</summary>
    public static string GetMapPath(string mapName) => $"maps/{mapName}_particles.txt";

    [GeneratedRegex("\"file\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileKey();

    /// <summary>Reads the <c>.pcf</c> paths of a manifest.</summary>
    /// <returns>The paths, relative to the game, with the leading precache marker removed.</returns>
    public static List<string> Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var paths = new List<string>();

        foreach (Match match in FileKey().Matches(text))
        {
            // A leading exclamation mark asks the game to precache the file
            var path = match.Groups["path"].Value.TrimStart('!').Replace('\\', '/');

            paths.Add(path);
        }

        return paths;
    }
}
