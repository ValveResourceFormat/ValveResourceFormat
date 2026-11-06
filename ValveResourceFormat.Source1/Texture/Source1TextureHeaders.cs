using System.IO;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Material;

namespace ValveResourceFormat.Source1.Texture;

/// <summary>Reads the header of a Source 1 texture without its pixels.</summary>
internal static class Source1TextureHeaders
{
    /// <summary>Reads the header of a texture at a path, or returns <see langword="null"/> when it is missing or not a texture.</summary>
    public static VtfHeader? TryRead(Source1FileSystem fileSystem, string texturePath)
    {
        try
        {
            using var stream = fileSystem.OpenFile(Source1Paths.ToGameTexturePath(texturePath));

            return stream == null ? null : VtfFile.ReadHeader(stream);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException or UnexpectedMagicException)
        {
            return null;
        }
    }

    /// <summary>Reads the header of the <c>$basetexture</c> of a material, or returns <see langword="null"/> when it has none.</summary>
    public static VtfHeader? TryReadBaseTexture(Source1FileSystem fileSystem, VmtFile vmt)
    {
        var baseTexture = vmt.Values.String("$basetexture");

        if (string.IsNullOrWhiteSpace(baseTexture))
        {
            return null;
        }

        return TryRead(fileSystem, Source1Paths.ToTexturePath(baseTexture));
    }
}
