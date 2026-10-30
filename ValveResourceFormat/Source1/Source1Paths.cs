namespace ValveResourceFormat.Source1;

/// <summary>
/// Helpers for the game relative paths that Source 1 files are opened by.
/// </summary>
internal static class Source1Paths
{
    private const string MaterialsFolder = "materials/";

    /// <summary>
    /// Converts backslashes to slashes, removes leading slashes and lower-cases the path.
    /// </summary>
    public static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

    /// <summary>
    /// Gets the path of a material from its name, such as <c>brick/wall</c> to <c>materials/brick/wall.vmt</c>.
    /// </summary>
    public static string ToMaterialPath(string name) => ToResourcePath(name, ".vmt");

    /// <summary>
    /// Gets the path of a texture from its name, such as <c>brick/wall</c> to <c>materials/brick/wall.vtf</c>.
    /// </summary>
    public static string ToTexturePath(string name) => ToResourcePath(name, ".vtf");

    private static string ToResourcePath(string name, string extension)
    {
        var path = Normalize(name);

        if (!path.StartsWith(MaterialsFolder, StringComparison.Ordinal))
        {
            path = MaterialsFolder + path;
        }

        if (!path.EndsWith(extension, StringComparison.Ordinal))
        {
            path += extension;
        }

        return path;
    }
}
