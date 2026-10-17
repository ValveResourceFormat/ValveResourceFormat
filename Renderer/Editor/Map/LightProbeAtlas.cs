using System.Linq;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// The probes of light probe volumes that were each baked into textures of their own, copied into one atlas laid out
/// like the one a compiled map bakes. Every fragment can then sample whichever volume it is in, as the game does with
/// each volume's own textures.
/// </summary>
/// <remarks>
/// The volumes sit side by side in width and height. Depth holds the six ambient cube faces of the irradiance one
/// after another, each as deep as the deepest volume, and the direct light shadows once.
/// </remarks>
public sealed class LightProbeAtlas
{
    // Block compressed textures are copied in whole blocks
    private const int BlockSize = 4;

    /// <summary>Gets the irradiance of every packed volume.</summary>
    public RenderTexture Irradiance { get; }

    /// <summary>Gets the direct light shadows of every packed volume, or <see langword="null"/> when not every volume has them.</summary>
    public RenderTexture? DirectLightShadows { get; }

    private LightProbeAtlas(RenderTexture irradiance, RenderTexture? directLightShadows)
    {
        Irradiance = irradiance;
        DirectLightShadows = directLightShadows;
    }

    private readonly record struct Placement(SceneLightProbe Volume, int X, int Y, int Width, int Height, int Depth);

    /// <summary>
    /// Copies the volumes into an atlas and points each at its place in it. A volume that cannot be copied loses its
    /// irradiance, so it is left out rather than sampled from the wrong place.
    /// </summary>
    /// <param name="volumes">The volumes, each with textures of its own.</param>
    /// <param name="logger">Where volumes that are left out are reported.</param>
    /// <returns>The atlas, or <see langword="null"/> when no volume was copied.</returns>
    public static LightProbeAtlas? Create(IReadOnlyList<SceneLightProbe> volumes, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(logger);

        var format = default(SizedInternalFormat?);
        var placements = new List<Placement>(volumes.Count);

        foreach (var volume in volumes)
        {
            var irradiance = volume.Irradiance!;
            var volumeFormat = GetInternalFormat(irradiance);

            format ??= volumeFormat;

            if (irradiance.Target != TextureTarget.Texture3D || irradiance.Depth % 6 != 0 || volumeFormat != format
                || (IsCompressed(irradiance) && (irradiance.Width % BlockSize != 0 || irradiance.Height % BlockSize != 0)))
            {
                logger.LogWarning("Light probe volume irradiance {Width}x{Height}x{Depth} ({Format}) does not fit the atlas, the volume is left out",
                    irradiance.Width, irradiance.Height, irradiance.Depth, volumeFormat);

                volume.Irradiance = null;
                continue;
            }

            placements.Add(new Placement(volume, 0, 0, irradiance.Width, irradiance.Height, irradiance.Depth / 6));
        }

        if (placements.Count == 0 || format is not { } atlasFormat)
        {
            return null;
        }

        // Rows of volumes, tallest first so each row wastes little height
        placements.Sort(static (a, b) => b.Height.CompareTo(a.Height));

        var maxSize = GL.GetInteger(GetPName.Max3DTextureSize);
        var (x, y, rowHeight, width) = (0, 0, 0, 0);

        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];

            if (x + placement.Width > maxSize)
            {
                y += rowHeight;
                (x, rowHeight) = (0, 0);
            }

            placements[i] = placement with { X = x, Y = y };

            x += AlignToBlock(placement.Width);
            rowHeight = Math.Max(rowHeight, AlignToBlock(placement.Height));
            width = Math.Max(width, x);
        }

        var height = y + rowHeight;
        var depth = placements.Max(static placement => placement.Depth);

        if (height > maxSize || depth * 6 > maxSize)
        {
            logger.LogError("Light probe volumes need a {Width}x{Height}x{Depth} atlas, larger than the GPU supports", width, height, depth * 6);
            return null;
        }

        var irradianceAtlas = CreateTexture(width, height, depth * 6, atlasFormat, "LightProbeAtlasIrradiance");

        foreach (var placement in placements)
        {
            for (var face = 0; face < 6; face++)
            {
                GL.CopyImageSubData(
                    placement.Volume.Irradiance!.Handle, ImageTarget.Texture3D, 0, 0, 0, face * placement.Depth,
                    irradianceAtlas.Handle, ImageTarget.Texture3D, 0, placement.X, placement.Y, face * depth,
                    placement.Width, placement.Height, placement.Depth);
            }
        }

        var shadowsAtlas = CreateShadowsAtlas(placements, width, height, depth, logger);

        foreach (var placement in placements)
        {
            var volume = placement.Volume;

            volume.Irradiance = irradianceAtlas;
            volume.DirectLightShadows = shadowsAtlas;
            volume.AtlasOffset = new Vector3(placement.X, placement.Y, 0);
            volume.AtlasSize = new Vector3(placement.Width, placement.Height, placement.Depth);
            volume.BakedTextureSize = new Vector3(placement.Width, placement.Height, placement.Depth * 6);
        }

        return new LightProbeAtlas(irradianceAtlas, shadowsAtlas);
    }

    // Only made when every volume has its shadows, in one format and as large as its probe grid
    private static RenderTexture? CreateShadowsAtlas(List<Placement> placements, int width, int height, int depth, ILogger logger)
    {
        var first = placements[0].Volume.DirectLightShadows;
        var format = first != null ? GetInternalFormat(first) : default;

        var fits = first != null && placements.TrueForAll(placement => placement.Volume.DirectLightShadows is { Target: TextureTarget.Texture3D } shadows
            && shadows.Width == placement.Width && shadows.Height == placement.Height && shadows.Depth == placement.Depth
            && GetInternalFormat(shadows) == format);

        if (!fits)
        {
            if (placements.Exists(static placement => placement.Volume.DirectLightShadows != null))
            {
                logger.LogWarning("Not every light probe volume has direct light shadows that fit the atlas, the volumes are left unshadowed");
            }

            return null;
        }

        var atlas = CreateTexture(width, height, depth, format, "LightProbeAtlasShadows");

        foreach (var placement in placements)
        {
            GL.CopyImageSubData(
                placement.Volume.DirectLightShadows!.Handle, ImageTarget.Texture3D, 0, 0, 0, 0,
                atlas.Handle, ImageTarget.Texture3D, 0, placement.X, placement.Y, 0,
                placement.Width, placement.Height, placement.Depth);
        }

        return atlas;
    }

    private static RenderTexture CreateTexture(int width, int height, int depth, SizedInternalFormat format, string label)
    {
        var texture = new RenderTexture(TextureTarget.Texture3D, width, height, depth, 1, label);

        GL.TextureStorage3D(texture.Handle, 1, format, width, height, depth);
        texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        texture.SetWrapMode(RsTextureAddressMode.Clamp);

        return texture;
    }

    private static int AlignToBlock(int size) => (size + BlockSize - 1) / BlockSize * BlockSize;

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
