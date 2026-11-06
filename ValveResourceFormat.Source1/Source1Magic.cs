namespace ValveResourceFormat.Source1;

/// <summary>
/// The first four bytes of the Source 1 files that are converted to Source 2 resources.
/// </summary>
internal static class Source1Magic
{
    /// <summary>"VTF" and a zero byte, the signature of a texture.</summary>
    public const uint Texture = 0x00465456;

    /// <summary>"IDST", the magic of a studio model.</summary>
    public const uint Model = 0x54534449;

    /// <summary>"VBSP", the magic of a map.</summary>
    public const uint Map = 0x50534256;

    /// <summary>
    /// Gets whether a file is a Source 1 texture, model, map or material.
    /// </summary>
    /// <param name="magic">The first four bytes of the file.</param>
    /// <param name="fileName">The name of the file, which tells a material apart since it is text.</param>
    public static bool IsSource1File(uint magic, string fileName)
        => magic is Texture or Model or Map
        || fileName.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase);
}
