using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat;
using ValveResourceFormat.Renderer.Editor.Selection;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Model;
using ValveResourceFormat.Source1.Texture;

namespace GUI.Utils;

/// <summary>
/// Finds the Source 1 game that a file being viewed belongs to.
/// </summary>
static class Source1Content
{
    private static readonly Source1GameCache<Source1FileSystem> SharedFileSystems = new();

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

        return SharedFileSystems.GetOrCreate(diskPath, () => Source1FileSystem.FromPath(diskPath));
    }

    /// <summary>
    /// A file system of the game for one viewer to own, for content that mounts files of its own such as a map.
    /// </summary>
    public static Source1FileSystem? Create(VrfGuiContext vrfGuiContext)
        => Source1FileSystem.FromPath(GetDiskPath(vrfGuiContext));

    /// <summary>
    /// Makes the file loader of a renderer context serve the files of the game as Source 2 resources.
    /// </summary>
    /// <returns>The provider, or <see langword="null"/> when the game is not known and nothing is served.</returns>
    public static Source1ResourceProvider? Mount(VrfGuiContext vrfGuiContext, Source1FileSystem? fileSystem)
    {
        if (fileSystem == null)
        {
            return null;
        }

        var provider = new Source1ResourceProvider(fileSystem, VrfGuiContext.Logger);
        vrfGuiContext.ResourceProvider = provider;

        return provider;
    }

    /// <summary>
    /// Whether the file is a Source 1 texture, model, map or material.
    /// </summary>
    public static bool IsAccepted(uint magic, string fileName) => Source1Magic.IsSource1File(magic, fileName);

    /// <summary>Gets the type of the Source 2 resource that a Source 1 file is converted to.</summary>
    /// <returns>The type, or <see cref="ResourceType.Unknown"/> when the file is not a Source 1 texture, model, map or material.</returns>
    public static ResourceType GetResourceType(uint magic, string fileName)
    {
        switch (magic)
        {
            case Source1Magic.Texture:
                return ResourceType.Texture;

            case Source1Magic.Model:
                return ResourceType.Model;

            case Source1Magic.Map:
                return ResourceType.Map;

            default:
                return IsAccepted(magic, fileName) ? ResourceType.Material : ResourceType.Unknown;
        }
    }

    /// <summary>A Source 1 file converted to a Source 2 resource.</summary>
    /// <param name="Resource">The converted resource.</param>
    /// <param name="OwnedFileSystem">The file system of the game that only this file uses, or <see langword="null"/>.</param>
    public record struct ConvertedFile(ValveResourceFormat.Resource Resource, Source1FileSystem? OwnedFileSystem);

    /// <summary>
    /// Mounts the game of the file on the context and converts the file to a Source 2 resource.
    /// </summary>
    /// <param name="vrfGuiContext">The context of the file, which serves the other files of the game afterwards.</param>
    /// <param name="stream">The stream of the file, or <see langword="null"/> to read it from disk.</param>
    /// <param name="magic">The first four bytes of the file.</param>
    /// <param name="fileSystem">The file system of the game to read from, owned by the caller, or <see langword="null"/> to find the game of the file.</param>
    public static ConvertedFile Convert(VrfGuiContext vrfGuiContext, Stream? stream, uint magic, Source1FileSystem? fileSystem = null)
    {
        switch (magic)
        {
            case Source1Magic.Texture:
                return new ConvertedFile(ConvertTexture(vrfGuiContext, stream), null);

            case Source1Magic.Model:
                return new ConvertedFile(ConvertModel(vrfGuiContext, stream, fileSystem), null);

            case Source1Magic.Map:
                return ConvertMap(vrfGuiContext, stream, fileSystem);

            default:
                return new ConvertedFile(LoadMaterial(vrfGuiContext, fileSystem), null);
        }
    }

    private static ValveResourceFormat.Resource ConvertTexture(VrfGuiContext vrfGuiContext, Stream? stream)
    {
        var input = OpenFile(vrfGuiContext, stream);

        try
        {
            var vtf = new VtfFile(input);
            var resource = Source1TextureConverter.Convert(vtf);
            resource.FileName = vrfGuiContext.FileName;

            return resource;
        }
        finally
        {
            if (stream == null)
            {
                input.Dispose();
            }
        }
    }

    private static ValveResourceFormat.Resource ConvertModel(VrfGuiContext vrfGuiContext, Stream? stream, Source1FileSystem? fileSystem)
    {
        fileSystem ??= GetShared(vrfGuiContext);
        var modelPath = GetGamePath(vrfGuiContext, fileSystem);

        var package = vrfGuiContext.CurrentPackage;
        var modelDirectory = Path.GetDirectoryName(vrfGuiContext.FileName);

        var opener = new Source1ModelFileOpener(modelPath, () => OpenFile(vrfGuiContext, stream), package, modelDirectory, fileSystem);
        var model = Source1Model.Load(modelPath, opener.Open);

        var provider = Mount(vrfGuiContext, fileSystem);

        var converted = Source1ModelConverter.Convert(model, modelPath, fileSystem, provider?.ModelMaterials);
        converted.Resource.FileName = vrfGuiContext.FileName;

        return converted.Resource;
    }

    private static ConvertedFile ConvertMap(VrfGuiContext vrfGuiContext, Stream? stream, Source1FileSystem? suppliedFileSystem)
    {
        vrfGuiContext.LoadingProgress?.Report("Reading map…");

        var input = OpenFile(vrfGuiContext, stream);
        BspFile bsp;

        try
        {
            bsp = BspFile.Open(input, Path.GetFileNameWithoutExtension(vrfGuiContext.FileName));
        }
        finally
        {
            if (stream == null)
            {
                input.Dispose();
            }
        }

        // Owned by the viewer, so the map can mount its pakfile without leaking it into other files of the game
#pragma warning disable CA2000 // Handed to the viewer in the result, and disposed here when the conversion fails
        var fileSystem = suppliedFileSystem ?? Create(vrfGuiContext)
            ?? throw new InvalidOperationException("A Source 1 map needs the files of its game.");
#pragma warning restore CA2000

        try
        {
            var provider = Mount(vrfGuiContext, fileSystem)!;
            var map = provider.LoadMap(bsp, vrfGuiContext.LoadingProgress);
            var mapPath = $"{map.MapPath}.vmap";
            var resource = provider.LoadFile(mapPath)
                ?? throw new InvalidDataException($"The converted map has no map '{mapPath}'.");
            resource.FileName = mapPath;

            return new ConvertedFile(resource, suppliedFileSystem == null ? fileSystem : null);
        }
        catch
        {
            if (suppliedFileSystem == null)
            {
                fileSystem.Dispose();
            }

            throw;
        }
    }

    private static ValveResourceFormat.Resource LoadMaterial(VrfGuiContext vrfGuiContext, Source1FileSystem? fileSystem)
    {
        fileSystem ??= GetShared(vrfGuiContext);
        var materialPath = Source1Paths.ToMaterialPath(GetGamePath(vrfGuiContext, fileSystem));

        Mount(vrfGuiContext, fileSystem);

        return vrfGuiContext.LoadFileCompiled(materialPath)
            ?? throw new InvalidDataException($"Failed to load material \"{materialPath}\".");
    }

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
