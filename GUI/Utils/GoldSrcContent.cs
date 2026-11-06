using System.Buffers.Binary;
using System.IO;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.GoldSrc;
using ValveResourceFormat.Source1;

namespace GUI.Utils;

/// <summary>
/// Converts the GoldSrc file being viewed to a Source 2 resource, and makes its context serve the other files of its game.
/// </summary>
static class GoldSrcContent
{
    private const int SpriteMagic = 0x50534449;

    /// <summary>Whether the file is a GoldSrc model, sprite or map, which are converted to Source 2 resources.</summary>
    /// <param name="magic">The first four bytes of the file.</param>
    /// <param name="version">The two bytes after the magic, the version of models and sprites.</param>
    /// <param name="fileName">The name of the file.</param>
    public static bool IsAccepted(uint magic, ushort version, string fileName)
        => GetResourceType(magic, version, fileName) != ResourceType.Unknown;

    /// <summary>Gets the type of the Source 2 resource that a GoldSrc file is converted to.</summary>
    /// <param name="magic">The first four bytes of the file.</param>
    /// <param name="version">The two bytes after the magic, the version of models and sprites.</param>
    /// <param name="fileName">The name of the file.</param>
    /// <returns>The type, or <see cref="ResourceType.Unknown"/> when the file is not a GoldSrc model, sprite or map.</returns>
    public static ResourceType GetResourceType(uint magic, ushort version, string fileName)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], version);

        if (GoldSrcMdlFile.IsModel(header))
        {
            return ResourceType.Model;
        }

        if (magic == SpriteMagic)
        {
            return version == SprFile.SupportedVersion ? ResourceType.Texture : ResourceType.Unknown;
        }

        if (GoldSrcBspFile.IsGoldSrcBsp(header) && fileName.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase))
        {
            return ResourceType.Map;
        }

        return ResourceType.Unknown;
    }

    /// <summary>Whether the file is a WAD, which is browsed as a package of textures.</summary>
    /// <param name="magic">The first four bytes of the file.</param>
    public static bool IsWad(uint magic)
    {
        Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, magic);

        return WadFile.IsWad(header);
    }

    /// <summary>Reads a WAD into an in-memory package with a converted Source 2 texture for each of its textures.</summary>
    /// <param name="fileName">The name of the WAD.</param>
    /// <param name="stream">The stream of the WAD, or <see langword="null"/> to read it from disk.</param>
    /// <returns>The package, which the caller owns.</returns>
    public static Package OpenWad(string fileName, Stream? stream)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var wad = stream == null ? WadFile.Open(fileName) : WadFile.Open(stream, name);
        var package = new Package();

        try
        {
            package.OptimizeEntriesForBinarySearch();

            foreach (var lump in wad.Lumps)
            {
                if (lump.Compression != 0)
                {
                    continue;
                }

                var image = wad.ReadImage(lump);

                if (image == null)
                {
                    continue;
                }

                var path = $"{lump.Name}.vtex_c";

                if (package.FindEntry(path) != null)
                {
                    continue;
                }

                using var texture = GoldSrcTextureConverter.Convert(image, image.IsMasked);
                using var data = new MemoryStream();
                texture.Serialize(data);
                package.AddFile(path, data.ToArray());
            }
        }
        catch
        {
            package.Dispose();
            throw;
        }

        return package;
    }

    /// <summary>Converts the file and mounts its game on the context.</summary>
    /// <param name="vrfGuiContext">The context of the file, which serves the other files of the game afterwards.</param>
    /// <param name="stream">The stream of the file, or <see langword="null"/> to read it from disk.</param>
    /// <param name="fileSystem">The file system of the game to read from, or <see langword="null"/> to find the game of the file.</param>
    /// <returns>The converted resource.</returns>
    public static ValveResourceFormat.Resource Convert(VrfGuiContext vrfGuiContext, Stream? stream, GoldSrcFileSystem? fileSystem = null)
    {
        byte[] data;

        if (stream == null)
        {
            data = File.ReadAllBytes(vrfGuiContext.FileName);
        }
        else
        {
            stream.Position = 0;
            data = Source1Streams.ReadAll(stream);
        }

        var diskPath = Source1Content.GetDiskPath(vrfGuiContext);
        fileSystem ??= GoldSrcFileSystem.FromPath(diskPath, VrfGuiContext.Logger);
        var provider = new GoldSrcResourceProvider(fileSystem, VrfGuiContext.Logger);
        vrfGuiContext.ResourceProvider = provider;

        var name = Path.GetFileNameWithoutExtension(vrfGuiContext.FileName);

        if (BinaryPrimitives.ReadInt32LittleEndian(data) == SpriteMagic)
        {
            var sprite = GoldSrcSpriteConverter.Convert(new SprFile(data));
            sprite.FileName = vrfGuiContext.FileName;

            return sprite;
        }

        if (GoldSrcMdlFile.IsModel(data))
        {
            var modelPath = GetGamePath(vrfGuiContext, fileSystem);
            var mdl = GoldSrcMdlFile.Load(data, name, companion => GoldSrcFileSystem.ReadCompanion(diskPath, companion, fileSystem));
            var model = provider.AddModel(mdl, modelPath);
            model.Resource!.FileName = vrfGuiContext.FileName;

            return model.Resource;
        }

        var map = provider.LoadMap(new GoldSrcBspFile(data, name), vrfGuiContext.LoadingProgress);
        var mapPath = $"{map.MapPath}.vmap";
        var resource = provider.LoadFile(mapPath)
            ?? throw new InvalidDataException($"The converted map has no map '{mapPath}'.");
        resource.FileName = mapPath;

        return resource;
    }

    // The path of the file in the game, such as models/scientist.mdl
    private static string GetGamePath(VrfGuiContext vrfGuiContext, GoldSrcFileSystem? fileSystem)
    {
        var fileName = vrfGuiContext.FileName;

        if (!Path.IsPathRooted(fileName))
        {
            return fileName.Replace('\\', '/');
        }

        if (fileSystem == null)
        {
            return Path.GetFileName(fileName);
        }

        var modRoot = Path.Combine(fileSystem.RootDirectory, fileSystem.ModDirectory);

        return Path.GetRelativePath(modRoot, fileName).Replace('\\', '/');
    }
}
