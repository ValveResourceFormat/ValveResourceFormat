using Microsoft.Extensions.Logging;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// The lightmaps of all faces of a GoldSrc map packed into one atlas. The light styles of a face that do not change
/// are added at their average brightness.
/// </summary>
internal sealed class GoldSrcLightmapAtlas
{
    // The atlas starts this wide and doubles its width, up to MaxSize, while the lightmaps do not fit. Its height is at most MaxSize.
    private const int MaxWidth = 1024;
    private const int MaxSize = 4096;
    private const int Border = 1;
    private const int BytesPerLuxel = 3;
    private const int WhiteSize = 2;

    // The engine scales a lightmap sample by 22 for each letter of a style pattern above 'a', and by 256 for a style
    // without a pattern. The sum becomes one of 1023 light levels after dividing it by 64.
    private const int LetterScale = 22;
    private const int NoPatternScale = 256;
    private const float SampleScaleToBrightness = 255f / (64f * 1023f);

    // The overbright factor that lets a lightmap brighten a texture to twice its color
    private const float Overbright = 2f;

    // The light that the engine curve and the overbright factor turn into a white that leaves the texture as it is
    private static readonly Vector3 UnlitLight = new(MathF.Pow(1f / Overbright, 1f / GoldSrcGamma.LightExponent));

    // The position is the first luxel of the lightmap, inside its border
    private readonly record struct Placement(int Width, int Height, int X, int Y, int MinS, int MinT);

    private readonly Placement[] placements;

    /// <summary>Gets the width of the atlas in luxels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the atlas in luxels.</summary>
    public int Height { get; }

    /// <summary>Gets the RGBA pixels of the atlas as half floats.</summary>
    public Half[] Pixels { get; }

    /// <summary>Gets the coordinates of a point that is lit fully, for faces that have no lightmap.</summary>
    public Vector2 WhiteCoordinates { get; }

    /// <summary>Gets the integer factor a lightmap sample of each light style is multiplied with, where 256 is full strength.</summary>
    /// <param name="entities">The entities of the map, whose lights set the patterns of the styles they switch.</param>
    public static int[] GetStyleSampleScales(IReadOnlyList<BspEntity> entities)
    {
        var scales = new int[byte.MaxValue + 1];
        Array.Fill(scales, NoPatternScale);

        for (var style = 0; style < BspLightStyles.DefaultPatternCount; style++)
        {
            scales[style] = GetPatternScale(BspLightStyles.GetDefaultPattern(style));
        }

        foreach (var entity in entities)
        {
            if (!BspLightStyles.TryGetLightStyle(entity, out var light))
            {
                continue;
            }

            if (light.StartsDisabled)
            {
                scales[light.Style] = 0;
            }
            else if (light.Pattern.Length > 0)
            {
                scales[light.Style] = GetPatternScale(light.Pattern);
            }
        }

        return scales;
    }

    private static int GetPatternScale(string pattern)
    {
        var sum = 0;

        foreach (var letter in pattern)
        {
            sum += (letter - 'a') * LetterScale;
        }

        return sum / pattern.Length;
    }

    /// <summary>Packs the lightmaps of a map.</summary>
    /// <param name="bsp">The map.</param>
    /// <param name="styleScales">The sample factor of each light style, see <see cref="GetStyleSampleScales"/>.</param>
    /// <param name="logger">Where lightmaps that do not fit are reported.</param>
    public GoldSrcLightmapAtlas(GoldSrcBspFile bsp, int[] styleScales, ILogger logger)
    {
        var faces = bsp.Faces;
        var lighting = bsp.Lighting;
        var textureInfos = bsp.TextureInfos;
        var extents = bsp.LightmapExtents;
        var candidates = new List<int>();

        placements = new Placement[faces.Length];

        for (var i = 0; i < faces.Length; i++)
        {
            var face = faces[i];

            if (!face.HasLightmap || face.TexInfo < 0 || face.TexInfo >= textureInfos.Length || textureInfos[face.TexInfo].IsSpecial)
            {
                continue;
            }

            var extent = extents[i];
            var byteCount = (long)extent.Width * extent.Height * BytesPerLuxel * face.LightStyleCount;

            if (extent.Width <= 0 || extent.Height <= 0 || extent.Width > MaxWidth - (Border * 2) || face.LightOffset < 0 || face.LightOffset + byteCount > lighting.Length)
            {
                continue;
            }

            candidates.Add(i);
        }

        candidates.Sort((a, b) => extents[b].Height.CompareTo(extents[a].Height));

        var width = MaxWidth;
        var packed = Pack(width, candidates, extents);

        while (packed.UnplacedCount > 0 && width < MaxSize)
        {
            width *= 2;
            packed = Pack(width, candidates, extents);
        }

        if (packed.UnplacedCount > 0)
        {
            logger.LogWarning("{Count} lightmaps do not fit in an atlas of {Size} luxels and are drawn fully lit", packed.UnplacedCount, MaxSize);
        }

        var (whiteX, whiteY) = packed.White;

        Width = width;
        Height = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(1, packed.UsedHeight));
        Pixels = new Half[Width * Height * 4];

        for (var y = 0; y < WhiteSize + (Border * 2); y++)
        {
            for (var x = 0; x < WhiteSize + (Border * 2); x++)
            {
                SetPixel(whiteX + x, whiteY + y, UnlitLight);
            }
        }

        WhiteCoordinates = new Vector2((whiteX + Border + (WhiteSize * 0.5f)) / Width, (whiteY + Border + (WhiteSize * 0.5f)) / Height);

        foreach (var faceIndex in candidates)
        {
            if (HasLightmap(faceIndex))
            {
                Copy(faces[faceIndex], placements[faceIndex], lighting, styleScales);
            }
        }
    }

    private readonly record struct PackResult(int UsedHeight, (int X, int Y) White, int UnplacedCount);

    // Places the white square and then the lightmaps, tallest first, in an atlas of the given width and at most MaxSize high
    private PackResult Pack(int atlasWidth, List<int> candidates, IReadOnlyList<GoldSrcLightmapExtents> extents)
    {
        Array.Clear(placements);

        var packer = new SkylinePacker(atlasWidth, MaxSize);
        var usedHeight = WhiteSize + (Border * 2);
        var unplacedCount = 0;

        packer.TryPack(usedHeight, usedHeight, out var whiteX, out var whiteY);

        foreach (var faceIndex in candidates)
        {
            var extent = extents[faceIndex];
            var paddedWidth = extent.Width + (Border * 2);
            var paddedHeight = extent.Height + (Border * 2);

            if (!packer.TryPack(paddedWidth, paddedHeight, out var x, out var y))
            {
                unplacedCount++;
                continue;
            }

            placements[faceIndex] = new Placement(extent.Width, extent.Height, x + Border, y + Border, extent.MinS, extent.MinT);
            usedHeight = Math.Max(usedHeight, y + paddedHeight);
        }

        return new PackResult(usedHeight, (whiteX, whiteY), unplacedCount);
    }

    /// <summary>Gets whether a face has a lightmap.</summary>
    public bool HasLightmap(int face) => placements[face].Width > 0;

    /// <summary>
    /// Gets the coordinates in the atlas of a point of a face, from its texture coordinates in texels.
    /// A luxel is 16 texels wide and its sample is at its center.
    /// </summary>
    public Vector2 GetCoordinates(int face, Vector2 textureCoordinates)
    {
        var placement = placements[face];

        var s = placement.X + ((textureCoordinates.X - (placement.MinS * 16f)) / 16f) + 0.5f;
        var t = placement.Y + ((textureCoordinates.Y - (placement.MinT * 16f)) / 16f) + 0.5f;

        return new Vector2(s / Width, t / Height);
    }

    private void Copy(in GoldSrcFace face, Placement placement, ReadOnlySpan<byte> lighting, int[] styleScales)
    {
        var luxelCount = placement.Width * placement.Height;
        var styleCount = face.LightStyleCount;

        for (var y = -Border; y < placement.Height + Border; y++)
        {
            var sourceY = Math.Clamp(y, 0, placement.Height - 1);

            for (var x = -Border; x < placement.Width + Border; x++)
            {
                var sourceX = Math.Clamp(x, 0, placement.Width - 1);
                var luxel = ((sourceY * placement.Width) + sourceX) * BytesPerLuxel;
                var light = Vector3.Zero;

                for (var slot = 0; slot < styleCount; slot++)
                {
                    var offset = face.LightOffset + (slot * luxelCount * BytesPerLuxel) + luxel;
                    var sample = new Vector3(lighting[offset], lighting[offset + 1], lighting[offset + 2]);
                    light += sample * (styleScales[face.GetLightStyle(slot)] * SampleScaleToBrightness / 255f);
                }

                SetPixel(placement.X + x, placement.Y + y, light);
            }
        }
    }

    private void SetPixel(int x, int y, Vector3 color)
    {
        var offset = ((y * Width) + x) * 4;

        Pixels[offset] = (Half)color.X;
        Pixels[offset + 1] = (Half)color.Y;
        Pixels[offset + 2] = (Half)color.Z;
        Pixels[offset + 3] = Half.One;
    }
}
