using System.IO;
using System.Text;
using System.Text.Unicode;
using ValveResourceFormat.IO;

namespace ValveResourceFormat.Source1;

/// <summary>
/// Helpers for the game relative paths that Source 1 files are opened by.
/// </summary>
internal static class Source1Paths
{
    private const string MaterialsFolder = "materials/";
    private const string GameModelExtension = ".mdl";
    private const string ModelExtension = ".vmdl";
    private const string GameSpriteExtension = ".spr";
    private const string VariantSeparator = "__v_";

    private const string GameMaterialExtension = ".vmt";
    private const string MaterialExtension = ".vmat";
    private const string GameTextureExtension = ".vtf";

    /// <summary>The extension of a converted texture.</summary>
    public const string TextureExtension = ".vtex";

    /// <summary>The extension of the high dynamic range variant of a converted texture.</summary>
    public const string HdrTextureExtension = ".hdr.vtex";

    /// <summary>The path of a texture that is one black pixel, which a surface without a base color reads.</summary>
    public const string BlackTexturePath = "materials/_black.vtex";

    private const string ToolsFolder = "materials/tools/";
    private const string ToolLabelKeyPrefix = "tool_";

    /// <summary>Gets the path of the generated texture that shows the label of a tools material.</summary>
    /// <param name="label">The label of a tools material.</param>
    public static string GetToolLabelTexturePath(ToolsMaterials.TextureLabel label) => string.Concat(ToolsFolder, label.Key, TextureExtension);

    /// <summary>Gets the label of a tools material from the path of the generated texture that shows it.</summary>
    /// <param name="path">The normalized path of the texture.</param>
    /// <param name="label">The label.</param>
    /// <returns>Whether the path is the path of a label texture.</returns>
    public static bool TryGetToolLabel(string path, out ToolsMaterials.TextureLabel label)
    {
        label = default;

        if (!path.StartsWith(ToolsFolder, StringComparison.Ordinal) || !path.EndsWith(TextureExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var name = path[ToolsFolder.Length..^TextureExtension.Length];

        if (!name.StartsWith(ToolLabelKeyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var toolsLabel = ToolsMaterials.GetToolsLabel(string.Concat(ToolsFolder, name[ToolLabelKeyPrefix.Length..], ".vmat"));

        if (toolsLabel == null)
        {
            return false;
        }

        label = toolsLabel.Value;

        return true;
    }

    /// <summary>The path of the model Source 2 draws in place of a model that is missing.</summary>
    public const string ErrorModelPath = "models/dev/error.vmdl";

    private const string GameSoundFolder = "sound/";
    private const string SoundFolder = "sounds/";
    private const string SoundExtension = ".vsnd";

    /// <summary>
    /// Gets the name of the sound a Source 2 sound path names, such as <c>sounds/ambient/wind.vsnd</c> to <c>ambient/wind</c>.
    /// </summary>
    /// <param name="path">The normalized path of the sound.</param>
    /// <param name="name">The path of the sound under the sound folder, without extension.</param>
    /// <returns>Whether the path is the path of a sound.</returns>
    public static bool TryGetSoundName(string path, out string name)
    {
        name = string.Empty;

        if (!path.StartsWith(SoundFolder, StringComparison.Ordinal) || !path.EndsWith(SoundExtension, StringComparison.Ordinal))
        {
            return false;
        }

        name = path[SoundFolder.Length..^SoundExtension.Length];

        return true;
    }

    // Leading characters of a wave name that select how the engine plays the file
    private static readonly char[] WavePrefixes = ['*', '?', '!', '#', '>', '<', '^', '@', ')', '(', '}', '&', '~', '`', '+', '$', '%'];

    /// <summary>Gets whether a name is the file name of a wave or an MP3 file rather than the name of a sound script entry.</summary>
    public static bool IsWaveName(string name)
    {
        return name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the name of a wave without its play mode prefix, in the normalized form, such as
    /// <c>)Weapons\357\357_reload1.wav</c> to <c>weapons/357/357_reload1.wav</c>.
    /// </summary>
    public static string NormalizeWaveName(string wave) => Normalize(wave.TrimStart(WavePrefixes));

    /// <summary>
    /// Gets the Source 2 sound path of a wave, such as <c>weapons\357\357_reload1.wav</c> to <c>sounds/weapons/357/357_reload1.vsnd</c>.
    /// </summary>
    public static string ToSoundPath(string wave) => string.Concat(SoundFolder, Path.ChangeExtension(NormalizeWaveName(wave), SoundExtension));

    /// <summary>
    /// Gets the path of the wave file of a sound, such as <c>ambient/wind</c> to <c>sound/ambient/wind.wav</c>.
    /// </summary>
    public static string ToWavePath(string name) => string.Concat(GameSoundFolder, name, ".wav");

    /// <summary>
    /// Gets the path of the MP3 file of a sound, such as <c>music/theme</c> to <c>sound/music/theme.mp3</c>.
    /// </summary>
    public static string ToMp3Path(string name) => string.Concat(GameSoundFolder, name, ".mp3");

    /// <summary>
    /// Decodes a name stored in a map as UTF-8, or as Latin-1 when the bytes are not valid UTF-8.
    /// </summary>
    public static string DecodeName(ReadOnlySpan<byte> bytes)
    {
        if (Utf8.IsValid(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Converts backslashes to slashes, removes leading slashes, lower-cases the path and resolves <c>..</c> segments
    /// against the folder before them. A <c>..</c> that leaves the start of the path stays, so <see cref="IsContained"/> refuses it.
    /// </summary>
    public static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

        if (!normalized.Contains("..", StringComparison.Ordinal))
        {
            return normalized;
        }

        var segments = new List<string>();

        foreach (var segment in normalized.Split('/'))
        {
            if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// Normalizes the path of a file and removes the <c>_c</c> suffix of compiled files.
    /// </summary>
    public static string NormalizeCompiled(string file)
    {
        var path = Normalize(file);

        if (path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
        {
            return path[..^GameFileLoader.CompiledFileSuffix.Length];
        }

        return path;
    }

    /// <summary>
    /// Gets whether a normalized path stays inside the folder it is opened from: it is not rooted and has no <c>..</c> segment.
    /// </summary>
    public static bool IsContained(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets whether a texture name is a render target such as <c>_rt_Camera</c>, which the engine draws and no file holds.
    /// </summary>
    public static bool IsRenderTarget(string name) => name.TrimStart('/', '\\').StartsWith("_rt_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the path Source 2 data names a model by, such as <c>models/props/crate.mdl</c> to <c>models/props/crate.vmdl</c>.
    /// </summary>
    public static string ToModelPath(string path) => ReplaceExtension(path, GameModelExtension, ModelExtension);

    /// <summary>
    /// Gets the path of the file of a model in the game from the path its converted model is served by, such as
    /// <c>models/props/crate.vmdl</c> to <c>models/props/crate.mdl</c>. Any other path is returned as it is.
    /// </summary>
    public static string ToGameModelPath(string path) => ReplaceExtension(path, ModelExtension, GameModelExtension);

    /// <summary>
    /// Gets the path of the file of a sprite in GoldSrc from the path its converted texture is served by, such as
    /// <c>sprites/glow01.vtex</c> to <c>sprites/glow01.spr</c>. Any other path is returned as it is.
    /// </summary>
    public static string ToGameSpritePath(string path) => ReplaceExtension(path, TextureExtension, GameSpriteExtension);

    /// <summary>
    /// Gets the path a converted material is served by, such as <c>brick/wall</c> to <c>materials/brick/wall.vmat</c>.
    /// </summary>
    public static string ToMaterialPath(string name) => ToResourcePath(name, MaterialExtension);

    /// <summary>
    /// Gets the path of the file of a material in the game, such as <c>brick/wall</c> or <c>materials/brick/wall.vmat</c> to <c>materials/brick/wall.vmt</c>.
    /// </summary>
    public static string ToGameMaterialPath(string name) => ToResourcePath(name, GameMaterialExtension);

    /// <summary>
    /// Gets the path of the file of a texture in the game from the path a converted texture is served by, such as
    /// <c>materials/brick/wall.vtex</c> to <c>materials/brick/wall.vtf</c>. Any other path is returned as it is.
    /// </summary>
    public static string ToGameTexturePath(string path) => ReplaceExtension(path, TextureExtension, GameTextureExtension);

    /// <summary>
    /// Gets the path a converted texture is served by from a texture name, such as <c>brick/wall</c> to <c>materials/brick/wall.vtex</c>.
    /// The final extension of the name is dropped first, so <c>brick/wall.tga</c> names the same texture.
    /// </summary>
    public static string ToTexturePath(string name)
    {
        var path = StripTextureExtension(Normalize(name));
        return AddMaterialsFolder(path) + TextureExtension;
    }

    /// <summary>
    /// Gets the path of the high dynamic range variant of a texture, or the path itself when it is not a plain texture path.
    /// </summary>
    public static string ToHdrTexturePath(string path)
    {
        if (!path.EndsWith(TextureExtension, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(HdrTextureExtension, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return ReplaceExtension(path, TextureExtension, HdrTextureExtension);
    }

    /// <summary>
    /// Gets the path of the plain variant of a high dynamic range texture, or the path itself when it is not one.
    /// </summary>
    public static string ToPlainTexturePath(string path) => ReplaceExtension(path, HdrTextureExtension, TextureExtension);

    /// <summary>
    /// Removes the final extension of a texture name, except for <c>.hdr</c>, which belongs to the name of a high dynamic range cubemap.
    /// </summary>
    public static string StripTextureExtension(string name)
    {
        if (name.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        var extensionStart = name.LastIndexOf('.');
        var folderEnd = name.AsSpan().LastIndexOfAny('/', '\\');

        if (extensionStart <= 0 || extensionStart < folderEnd)
        {
            return name;
        }

        return name[..extensionStart];
    }

    /// <summary>
    /// Names a variant of a file by putting its name before the extension, such as the frame of a texture or the shader of a material.
    /// </summary>
    public static string AddVariant(string path, string variant)
    {
        var extension = Path.GetExtension(path);

        var stem = path[..^extension.Length];

        return stem + VariantSeparator + variant + extension;
    }

    /// <summary>
    /// Splits the name of a variant off a path made by <see cref="AddVariant"/>.
    /// </summary>
    /// <returns>The path of the file the variant is made from.</returns>
    public static string RemoveVariant(string path, out string? variant)
    {
        var extension = Path.GetExtension(path);
        var stem = path.AsSpan(0, path.Length - extension.Length);
        var separator = stem.LastIndexOf(VariantSeparator, StringComparison.Ordinal);

        if (separator < 0)
        {
            variant = null;
            return path;
        }

        variant = stem[(separator + VariantSeparator.Length)..].ToString();
        return string.Concat(stem[..separator], extension);
    }

    private static string AddMaterialsFolder(string path)
    {
        if (path.StartsWith(MaterialsFolder, StringComparison.Ordinal))
        {
            return path;
        }

        return MaterialsFolder + path;
    }

    private static string ToResourcePath(string name, string extension)
    {
        var path = AddMaterialsFolder(Normalize(name));

        return RemoveMaterialExtension(path) + extension;
    }

    private static string RemoveMaterialExtension(string path)
    {
        if (path.EndsWith(GameMaterialExtension, StringComparison.Ordinal))
        {
            return path[..^GameMaterialExtension.Length];
        }

        if (path.EndsWith(MaterialExtension, StringComparison.Ordinal))
        {
            return path[..^MaterialExtension.Length];
        }

        return path;
    }

    // Swaps the extension of a path that ends with one, and returns any other path as it is
    private static string ReplaceExtension(string path, string extension, string newExtension)
    {
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return string.Concat(path.AsSpan(0, path.Length - extension.Length), newExtension);
    }
}
