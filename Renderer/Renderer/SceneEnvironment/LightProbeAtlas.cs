using System.Linq;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.Utils;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Light probe textures shared by all volumes in a scene, each volume sampling its own region. Either the atlas baked
/// into a compiled map, or one packed at load time from volumes that have separate textures.
/// </summary>
public sealed class LightProbeAtlas
{
    /// <summary>Number of ambient cube faces stacked along the irradiance depth.</summary>
    private const int IrradianceFaces = 6;

    /// <summary>Gets the irradiance texture.</summary>
    public required RenderTexture Irradiance { get; init; }

    /// <summary>Gets the direct light index texture (lighting version 8.1), or <see langword="null"/> if no volume has one.</summary>
    public RenderTexture? DirectLightIndices { get; init; }

    /// <summary>Gets the direct light scalar texture (lighting version 8.1), or <see langword="null"/> if no volume has one.</summary>
    public RenderTexture? DirectLightScalars { get; init; }

    /// <summary>Gets the direct light shadow texture (lighting version 8.2), or <see langword="null"/> if no volume has one.</summary>
    public RenderTexture? DirectLightShadows { get; init; }

    /// <summary>Gets whether the textures were packed at load time.</summary>
    public bool IsPacked { get; private init; }

    /// <summary>Deletes the textures if they were packed at load time. Baked textures belong to the material loader.</summary>
    public void Delete()
    {
        if (!IsPacked)
        {
            return;
        }

        Irradiance.Delete();
        DirectLightIndices?.Delete();
        DirectLightScalars?.Delete();
        DirectLightShadows?.Delete();
    }

    /// <summary>Wraps the atlas baked into a compiled map.</summary>
    /// <param name="volume">Any volume that samples the baked atlas.</param>
    /// <returns>The atlas, or <see langword="null"/> when the volume has no irradiance.</returns>
    public static LightProbeAtlas? FromBaked(SceneLightProbe volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return volume.Irradiance == null ? null : new LightProbeAtlas
        {
            Irradiance = volume.Irradiance,
            DirectLightIndices = volume.DirectLightIndices,
            DirectLightScalars = volume.DirectLightScalars,
            DirectLightShadows = volume.DirectLightShadows,
        };
    }

    /// <summary>
    /// Copies volumes with separate textures into one atlas, and points each volume at its region. Volumes that do
    /// not fit or are incompatible are skipped. A direct light texture missing on some volumes is zero there.
    /// </summary>
    /// <param name="volumes">Volumes with separate textures.</param>
    /// <param name="materialLoader">Material loader the volume textures are evicted from.</param>
    /// <param name="logger">Logger for skipped volumes.</param>
    /// <returns>The atlas, or <see langword="null"/> if nothing was packed.</returns>
    public static LightProbeAtlas? Pack(IReadOnlyList<SceneLightProbe> volumes, MaterialLoader materialLoader, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(materialLoader);
        ArgumentNullException.ThrowIfNull(logger);

        var irradianceFormat = default(SizedInternalFormat?);
        var candidates = new List<(SceneLightProbe Volume, (int Width, int Height, int Depth) Grid)>(volumes.Count);

        foreach (var volume in volumes)
        {
            if (volume.Irradiance is not { Target: TextureTarget.Texture3D } irradiance || irradiance.Depth % IrradianceFaces != 0)
            {
                logger.LogWarning("Skipping light probe volume, irradiance depth is not a multiple of {Faces}", IrradianceFaces);
                continue;
            }

            var format = GetInternalFormat(irradiance);
            irradianceFormat ??= format;

            if (format != irradianceFormat || !CanCopy(irradiance))
            {
                logger.LogWarning("Skipping light probe volume, irradiance {Width}x{Height}x{Depth} ({Format}) is incompatible with the atlas",
                    irradiance.Width, irradiance.Height, irradiance.Depth, format);
                continue;
            }

            candidates.Add((volume, (irradiance.Width, irradiance.Height, irradiance.Depth / IrradianceFaces)));
        }

        if (irradianceFormat is not { } atlasFormat || candidates.Count == 0)
        {
            return null;
        }

        var maxSize = GL.GetInteger(GetPName.Max3DTextureSize);
        var slots = LightProbeAtlasPacker.Pack([.. candidates.Select(static candidate => candidate.Grid)], maxSize, maxSize / IrradianceFaces, out var atlasSize);

        var placed = candidates
            .Zip(slots, static (candidate, slot) => (candidate.Volume, candidate.Grid, Slot: slot))
            .Where(static placement => placement.Slot != null)
            .Select(static placement => new Placement(placement.Volume, placement.Grid, placement.Slot!.Value))
            .ToList();

        if (placed.Count < candidates.Count)
        {
            logger.LogWarning("Only {Placed} of {Count} light probe volumes fit in the atlas, skipping the rest", placed.Count, candidates.Count);
        }

        if (placed.Count == 0)
        {
            return null;
        }

        var irradianceAtlas = CreateTexture(atlasFormat, atlasSize.Width, atlasSize.Height, atlasSize.Depth * IrradianceFaces, "LightProbeAtlasIrradiance");
        var copied = new HashSet<RenderTexture>();

        foreach (var placement in placed)
        {
            var (_, grid, slot) = placement;
            copied.Add(placement.Volume.Irradiance!);

            // Each face slice is copied into the matching face slice of the atlas
            for (var face = 0; face < IrradianceFaces; face++)
            {
                GL.CopyImageSubData(
                    placement.Volume.Irradiance!.Handle, ImageTarget.Texture3D, 0, 0, 0, face * grid.Depth,
                    irradianceAtlas.Handle, ImageTarget.Texture3D, 0, slot.X, slot.Y, (face * atlasSize.Depth) + slot.Z,
                    grid.Width, grid.Height, grid.Depth);
            }
        }

        var atlas = new LightProbeAtlas
        {
            Irradiance = irradianceAtlas,
            DirectLightIndices = PackDirectLight(placed, static volume => volume.DirectLightIndices, atlasSize, "LightProbeAtlasIndices", copied, logger),
            DirectLightScalars = PackDirectLight(placed, static volume => volume.DirectLightScalars, atlasSize, "LightProbeAtlasScalars", copied, logger),
            DirectLightShadows = PackDirectLight(placed, static volume => volume.DirectLightShadows, atlasSize, "LightProbeAtlasShadows", copied, logger),
            IsPacked = true,
        };

        // Indices are light IDs and must not be filtered
        atlas.DirectLightIndices?.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

        foreach (var (volume, grid, slot) in placed)
        {
            volume.Irradiance = atlas.Irradiance;
            volume.DirectLightIndices = atlas.DirectLightIndices ?? volume.DirectLightIndices;
            volume.DirectLightScalars = atlas.DirectLightScalars ?? volume.DirectLightScalars;
            volume.DirectLightShadows = atlas.DirectLightShadows ?? volume.DirectLightShadows;
            volume.AtlasOffset = new Vector3(slot.X, slot.Y, slot.Z);
            volume.AtlasSize = new Vector3(grid.Width, grid.Height, grid.Depth);
        }

        // Only textures cached by the material loader are deleted, others are owned elsewhere
        foreach (var texture in copied)
        {
            materialLoader.EvictTexture(texture);
        }

        logger.LogDebug("Packed {Count} light probe volumes into a {Width}x{Height}x{Depth} light probe atlas",
            placed.Count, atlasSize.Width, atlasSize.Height, atlasSize.Depth);

        return atlas;
    }

    private readonly record struct Placement(SceneLightProbe Volume, (int Width, int Height, int Depth) Grid, LightProbeAtlasSlot Slot);

    // One texel per probe. Zeroed first so volumes without this texture read as no direct light
    private static RenderTexture? PackDirectLight(List<Placement> placed, Func<SceneLightProbe, RenderTexture?> getTexture,
        (int Width, int Height, int Depth) atlasSize, string label, HashSet<RenderTexture> copied, ILogger logger)
    {
        var sources = placed
            .Select(placement => (placement, Texture: getTexture(placement.Volume)))
            .Where(source => source.Texture != null && Covers(source.Texture, source.placement.Grid))
            .ToList();

        if (sources.Count == 0)
        {
            return null;
        }

        var format = GetInternalFormat(sources[0].Texture!);
        var atlas = CreateTexture(format, atlasSize.Width, atlasSize.Height, atlasSize.Depth, label);
        ClearToZero(atlas, format);

        foreach (var ((_, grid, slot), texture) in sources)
        {
            if (GetInternalFormat(texture!) != format || !CanCopy(texture!))
            {
                logger.LogWarning("Skipping light probe volume {Label} texture {Width}x{Height}x{Depth}, incompatible with the atlas",
                    label, texture!.Width, texture.Height, texture.Depth);
                continue;
            }

            GL.CopyImageSubData(
                texture!.Handle, ImageTarget.Texture3D, 0, 0, 0, 0,
                atlas.Handle, ImageTarget.Texture3D, 0, slot.X, slot.Y, slot.Z,
                grid.Width, grid.Height, grid.Depth);

            copied.Add(texture);
        }

        return atlas;
    }

    // Direct light textures have one texel per probe; anything else (e.g. the 1x1x1 default) has nothing to copy
    private static bool Covers(RenderTexture texture, (int Width, int Height, int Depth) grid)
        => texture.Target == TextureTarget.Texture3D && texture.Width == grid.Width && texture.Height == grid.Height && texture.Depth == grid.Depth;

    // Compressed copies must cover whole blocks since they do not end at the atlas edge
    private static bool CanCopy(RenderTexture texture)
        => !IsCompressed(texture) || (texture.Width % LightProbeAtlasPacker.BlockSize == 0 && texture.Height % LightProbeAtlasPacker.BlockSize == 0);

    private static RenderTexture CreateTexture(SizedInternalFormat format, int width, int height, int depth, string label)
    {
        // Single mip: probes are sampled at level 0, and lower mips would not stay block aligned
        var texture = new RenderTexture(TextureTarget.Texture3D, width, height, depth, 1, label);
        GL.TextureStorage3D(texture.Handle, 1, format, width, height, depth);
        texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        texture.SetWrapMode(RsTextureAddressMode.Clamp);

        return texture;
    }

    // glClearTexImage does not support compressed formats, but all-zero BC blocks decode to zero
    private static void ClearToZero(RenderTexture texture, SizedInternalFormat format)
    {
        if (!IsCompressed(texture))
        {
            GL.ClearTexImage(texture.Handle, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            return;
        }

        GL.GetTextureLevelParameter(texture.Handle, 0, GetTextureParameter.TextureCompressedImageSize, out int size);

        var zeros = new byte[size];
        GL.CompressedTextureSubImage3D(texture.Handle, 0, 0, 0, 0, texture.Width, texture.Height, texture.Depth, (PixelFormat)format, size, zeros);
    }

    private static SizedInternalFormat GetInternalFormat(RenderTexture texture)
    {
        GL.GetTextureLevelParameter(texture.Handle, 0, GetTextureParameter.TextureInternalFormat, out int format);
        return (SizedInternalFormat)format;
    }

    private static bool IsCompressed(RenderTexture texture)
    {
        GL.GetTextureLevelParameter(texture.Handle, 0, GetTextureParameter.TextureCompressed, out int compressed);
        return compressed != 0;
    }
}
