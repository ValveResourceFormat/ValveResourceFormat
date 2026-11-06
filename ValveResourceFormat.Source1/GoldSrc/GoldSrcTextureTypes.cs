using System.IO;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// The texture types of a GoldSrc game, read from its <c>sound/materials.txt</c>, which gives each texture name a letter
/// for the material it is made of.
/// </summary>
internal sealed class GoldSrcTextureTypes
{
    private const string FilePath = "sound/materials.txt";
    private const int NameLength = 12;

    private readonly Dictionary<string, char> types = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the texture types of a game.</summary>
    /// <param name="fileSystem">The game, or <see langword="null"/> when the map is not in a known game.</param>
    /// <returns>The types, which are empty when the game has no file.</returns>
    public static GoldSrcTextureTypes Load(GoldSrcFileSystem? fileSystem)
    {
        var textureTypes = new GoldSrcTextureTypes();
        var path = fileSystem?.FindFile(FilePath);

        if (path == null)
        {
            return textureTypes;
        }

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                textureTypes.AddLine(line);
            }
        }
        catch (IOException)
        {
        }

        return textureTypes;
    }

    // A line is a letter and a texture name. Lines that start with anything else are comments.
    private void AddLine(string line)
    {
        var text = line.AsSpan().TrimStart();

        if (text.Length == 0 || !char.IsAsciiLetter(text[0]))
        {
            return;
        }

        var type = char.ToUpperInvariant(text[0]);
        var rest = text[1..].TrimStart();
        var end = rest.IndexOfAny(" \t");
        var name = end < 0 ? rest : rest[..end];

        if (name.Length == 0)
        {
            return;
        }

        if (name.Length > NameLength)
        {
            name = name[..NameLength];
        }

        types.TryAdd(name.ToString(), type);
    }

    /// <summary>Finds the surface of a texture by the type of its name.</summary>
    /// <param name="textureName">The name of the texture as the map stores it.</param>
    /// <returns>The name of the surface, see <see cref="GoldSrcSurfaceProperties.GetSurfaceName"/>.</returns>
    public string FindSurface(string textureName)
    {
        var name = textureName.AsSpan();

        if (name.Length > 0 && name[0] is '-' or '+')
        {
            name = name[Math.Min(2, name.Length)..];
        }

        if (name.Length > 0 && name[0] is '{' or '!' or '~' or ' ')
        {
            name = name[1..];
        }

        if (name.Length > NameLength)
        {
            name = name[..NameLength];
        }

        var lookup = types.GetAlternateLookup<ReadOnlySpan<char>>();
        lookup.TryGetValue(name, out var type);

        return GoldSrcSurfaceProperties.GetSurfaceName(type);
    }
}
