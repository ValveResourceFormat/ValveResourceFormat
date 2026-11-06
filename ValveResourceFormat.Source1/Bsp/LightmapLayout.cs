using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp;

internal readonly record struct LightmapRect(int X, int Y, int Width, int Height);

/// <summary>
/// The lightmap pixels of a map as four RGBA16F pages of identical size and layout, holding linear radiance. The
/// alpha is how much of the page the sun lights directly, from 0 to 1, or 0 when the map does not store it. Page 0 is
/// the flat lightmap. Pages 1 to 3 are the bump basis lightmaps of faces with
/// <see cref="SurfFlags.BumpLight"/>, and copies of page 0 for all other faces. One lightmap UV addresses the
/// same luxel in every page.
/// </summary>
/// <remarks>
/// A shader that bump maps with the Source bump basis blends the pages with weights
/// <c>dp[i] = saturate(dot(normalTS, basis[i]))^2</c>:
/// <c>light = (dp[0] * page1 + dp[1] * page2 + dp[2] * page3) / (dp[0] + dp[1] + dp[2])</c>.
/// The basis vectors are in tangent space where X is along the texture U axis, Y along the texture V axis
/// and Z is the surface normal (see <see cref="BspVertex.Tangent"/>), see <see cref="BumpBasis"/>.
/// The bump sets are scaled so that their mean is the flat set, in the color and in the sun amount.
/// The light styles of a face are added at their brightness when the map starts, see <see cref="BspLightStyles"/>.
/// The styles that dynamic lights play are not added.
/// </remarks>
internal sealed class LightmapAtlas
{
    /// <summary>The number of pages: flat light plus the three bump basis lights.</summary>
    public const int PageCount = 4;

    /// <summary>The tangent space direction each of pages 1 to 3 was lit from.</summary>
    public static IReadOnlyList<Vector3> BumpBasis { get; } =
    [
        new Vector3(0.81649661064147949f, 0f, 0.57735025882720947f),
        new Vector3(-0.40824821591377258f, 0.70710676908493042f, 0.57735025882720947f),
        new Vector3(-0.40824821591377258f, -0.70710676908493042f, 0.57735025882720947f),
    ];

    public int Width { get; }

    public int Height { get; }

    /// <summary>The four pages, each <c>Width * Height * 4</c> halfs.</summary>
    public Half[][] Pages { get; }

    public LightmapAtlas(int width, int height, Half[][] pages)
    {
        if (pages.Length != PageCount)
        {
            throw new ArgumentException($"An atlas needs {PageCount} pages.", nameof(pages));
        }

        Width = width;
        Height = height;
        Pages = pages;
    }
}

/// <summary>
/// Packs the lightmaps of all faces of one lightmap lump into one atlas. Every face rectangle has a border of
/// <see cref="Padding"/> texels that repeats the nearest edge luxel, so bilinear sampling never reads a neighbour.
/// </summary>
/// <remarks>
/// Lightmap UVs address luxel centres: luxel <c>i</c> of a face sits at texel <c>rect.X + i</c> and the UV of its
/// centre is <c>(rect.X + i + 0.5) / Width</c>.
/// </remarks>
internal sealed class LightmapLayout
{
    /// <summary>The number of border texels around every face rectangle.</summary>
    public const int Padding = 2;

    private const int MaxLightStyles = 4;
    private const int MaxAtlasSize = 8192;
    private const int PackingAttempts = 3;

    private readonly BspFile bsp;
    private LightmapRect[]? packing;
    private int width;
    private int height;

    /// <summary>Whether this layout reads the HDR lump.</summary>
    public bool IsHdr { get; }

    public int Width
    {
        get
        {
            FindPacking();
            return width;
        }
    }

    public int Height
    {
        get
        {
            FindPacking();
            return height;
        }
    }

    /// <summary>The UV of a white texel for faces without a lightmap.</summary>
    public Vector2 DefaultUv
    {
        get
        {
            FindPacking();
            var rect = packing![^1];
            return new Vector2((rect.X + 0.5f) / width, (rect.Y + 0.5f) / height);
        }
    }

    internal LightmapLayout(BspFile bsp, bool hdr)
    {
        this.bsp = bsp;
        IsHdr = hdr;
    }

    private BspFace[] Faces => bsp.GetLightmappedFaces(IsHdr);

    private readonly record struct Packable(int Index, int Width, int Height);

    private static int GetWidth(int sizeIndex) => 1 << ((sizeIndex + 1) >> 1);

    private static int GetHeight(int sizeIndex) => 1 << (sizeIndex >> 1);

    private bool TryPacking(int initialWidth, int initialHeight, List<Packable> toPack, LightmapRect[] result)
    {
        if (initialWidth > MaxAtlasSize || initialHeight > MaxAtlasSize)
        {
            return false;
        }

        var packer = new SkylinePacker(initialWidth, initialHeight);

        foreach (var item in toPack)
        {
            if (!packer.TryPack(item.Width, item.Height, out var x, out var y))
            {
                return false;
            }

            result[item.Index] = new LightmapRect(x + Padding, y + Padding, item.Width - (2 * Padding), item.Height - (2 * Padding));
        }

        width = initialWidth;
        height = initialHeight;
        return true;
    }

    private void FindPacking()
    {
        if (packing != null)
        {
            return;
        }

        var faces = Faces;
        var result = new LightmapRect[faces.Length + 1];
        var toPack = new List<Packable>();
        long area = 0;

        for (var i = 0; i < faces.Length; i++)
        {
            var face = faces[i];

            if (!face.HasLightmap || face.LightMapSizeX < 0 || face.LightMapSizeY < 0)
            {
                continue;
            }

            var packable = new Packable(i, face.LightMapSizeX + 1 + (2 * Padding), face.LightMapSizeY + 1 + (2 * Padding));
            toPack.Add(packable);
            area += (long)packable.Width * packable.Height;
        }

        var defaultPackable = new Packable(faces.Length, 1 + (2 * Padding), 1 + (2 * Padding));
        toPack.Add(defaultPackable);
        area += (long)defaultPackable.Width * defaultPackable.Height;

        toPack.Sort((a, b) =>
        {
            var keyA = ((long)a.Width * 65536) + a.Height;
            var keyB = ((long)b.Width * 65536) + b.Height;
            return keyB.CompareTo(keyA);
        });

        var sizeIndex = 1;

        while (GetWidth(sizeIndex) * (long)GetHeight(sizeIndex) < area)
        {
            sizeIndex++;
        }

        for (var attempt = 0; attempt < PackingAttempts; attempt++, sizeIndex++)
        {
            if (TryPacking(GetWidth(sizeIndex), GetHeight(sizeIndex), toPack, result))
            {
                packing = result;
                return;
            }
        }

        throw new InvalidDataException("Unable to pack lightmap.");
    }

    /// <summary>Gets the luxel rectangle of a face, without the padding. The rectangle is empty when the face has no lightmap.</summary>
    public LightmapRect GetLightmapRegion(int faceIndex)
    {
        FindPacking();
        return packing![faceIndex];
    }

    /// <summary>Whether the face stores four lightmaps (flat plus the three bump basis lightmaps).</summary>
    public bool IsBumped(int faceIndex)
    {
        var face = Faces[faceIndex];
        return face.TexInfo >= 0
            && face.TexInfo < bsp.TextureInfos.Length
            && (bsp.TextureInfos[face.TexInfo].Flags & SurfFlags.BumpLight) != 0;
    }

    /// <summary>
    /// Gets the UV range of the luxel centres of a face. A vertex UV is <c>min + size * t</c> with <c>t</c> the vertex
    /// position in luxels from the face lightmap mins divided by the lightmap size.
    /// </summary>
    public void GetUvs(int faceIndex, out Vector2 min, out Vector2 size)
    {
        var rect = GetLightmapRegion(faceIndex);
        var scaleX = 1f / Width;
        var scaleY = 1f / Height;

        min = new Vector2((rect.X + 0.5f) * scaleX, (rect.Y + 0.5f) * scaleY);
        size = new Vector2((rect.Width - 1f) * scaleX, (rect.Height - 1f) * scaleY);
    }

    /// <summary>Builds the four pages of the atlas.</summary>
    public LightmapAtlas BuildAtlas()
    {
        FindPacking();

        var atlasWidth = width;
        var atlasHeight = height;
        var pages = new Half[LightmapAtlas.PageCount][];

        for (var page = 0; page < pages.Length; page++)
        {
            pages[page] = new Half[atlasWidth * atlasHeight * 4];
        }

        var faces = Faces;
        var lighting = bsp.GetLightmapSamples(IsHdr).Span;
        var styleBrightness = BspLightStyles.GetDefaultBrightness(bsp.Entities);
        var dynamicStyles = BspLightStyles.GetDynamicStyles(bsp.Entities);
        var mapFlags = bsp.MapFlags;
        var hasExtraBlock = (mapFlags & BspMapFlags.LightmapAlpha) != 0;
        var hasSunAmounts = hasExtraBlock && (mapFlags & BspMapFlags.LightmapAlpha3) != 0;
        var stylesStepOverExtraBlock = hasExtraBlock && (mapFlags & BspMapFlags.LightStylesWithSunAmount) != 0;

        FillWhite(pages, packing![faces.Length], atlasWidth);

        for (var i = 0; i < faces.Length; i++)
        {
            var face = faces[i];
            var rect = packing[i];

            if (!face.HasLightmap || rect.Width <= 0)
            {
                continue;
            }

            var sampleCount = rect.Width * rect.Height;
            var setSize = sampleCount * 4;
            var setCount = IsBumped(i) ? 4 : 1;
            var styleSetCount = stylesStepOverExtraBlock ? setCount + 1 : setCount;
            var sunAmounts = hasSunAmounts ? FindSunAmounts(lighting, face, setCount, styleSetCount, setSize) : [];
            var availableSets = 0;

            if (face.LightOffset >= 0 && face.LightOffset < lighting.Length)
            {
                availableSets = Math.Min(setCount, (lighting.Length - face.LightOffset) / setSize);
            }

            if (availableSets == 0)
            {
                FillWhite(pages, rect, atlasWidth);
                continue;
            }

            // A flat face has the same luxels on every page
            var computedPages = setCount == 4 ? pages.Length : 1;
            var pageLuxels = new Vector4[computedPages][];

            for (var page = 0; page < computedPages; page++)
            {
                var set = setCount == 4 ? page : 0;

                if (set >= availableSets)
                {
                    set = 0;
                }

                var luxels = new Vector4[sampleCount];

                // The sun amount of a bumped face has a byte per set, a flat face only the first
                if (sunAmounts.Length > 0)
                {
                    var sunAmountByte = setCount == 4 ? page : 0;

                    for (var luxel = 0; luxel < luxels.Length; luxel++)
                    {
                        luxels[luxel].W = sunAmounts[(luxel * 4) + sunAmountByte] / 255f;
                    }
                }

                // The lightmaps of the styles of a face follow each other, each with its own flat and bump basis sets
                for (var slot = 0; slot < MaxLightStyles; slot++)
                {
                    var style = face.GetLightStyle(slot);

                    if (style == BspLightStyles.None)
                    {
                        break;
                    }

                    var offset = face.LightOffset + (((slot * styleSetCount) + set) * setSize);
                    var brightness = styleBrightness[style];

                    if (dynamicStyles[style] || brightness <= 0f || offset + setSize > lighting.Length)
                    {
                        continue;
                    }

                    var samples = MemoryMarshal.Cast<byte, ColorRGBExp32>(lighting.Slice(offset, setSize));

                    for (var luxel = 0; luxel < samples.Length; luxel++)
                    {
                        luxels[luxel] += new Vector4(samples[luxel].ToLinear() * brightness, 0f);
                    }
                }

                pageLuxels[page] = luxels;
            }

            if (setCount == 4)
            {
                NormalizeBumpSets(pageLuxels);
            }

            for (var page = 0; page < computedPages; page++)
            {
                WritePadded(pages, page, computedPages, rect, pageLuxels[page], atlasWidth);
            }
        }

        LightmapSeamStitcher.Stitch(bsp, faces, this, pages, atlasWidth);

        return new LightmapAtlas(atlasWidth, atlasHeight, pages);
    }

    // The bump sets of a luxel are scaled per channel so that their mean is the flat set, in the color and in the sun
    // amount. A channel whose bump sets are all 0 stays 0.
    private static void NormalizeBumpSets(Vector4[][] pageLuxels)
    {
        var flat = pageLuxels[0];

        for (var luxel = 0; luxel < flat.Length; luxel++)
        {
            var bumpMean = (pageLuxels[1][luxel] + pageLuxels[2][luxel] + pageLuxels[3][luxel]) / 3f;
            var scale = Vector4.Zero;

            if (bumpMean.X != 0f)
            {
                scale.X = flat[luxel].X / bumpMean.X;
            }

            if (bumpMean.Y != 0f)
            {
                scale.Y = flat[luxel].Y / bumpMean.Y;
            }

            if (bumpMean.Z != 0f)
            {
                scale.Z = flat[luxel].Z / bumpMean.Z;
            }

            if (bumpMean.W != 0f)
            {
                scale.W = flat[luxel].W / bumpMean.W;
            }

            for (var page = 1; page < pageLuxels.Length; page++)
            {
                pageLuxels[page][luxel] *= scale;
            }
        }
    }

    // The extra block of the lightmaps of the default style holds the sun amounts. It follows the sets of that style,
    // which is the first one when later styles do not step over the block.
    private static ReadOnlySpan<byte> FindSunAmounts(ReadOnlySpan<byte> lighting, BspFace face, int setCount, int styleSetCount, int setSize)
    {
        for (var slot = 0; slot < MaxLightStyles; slot++)
        {
            var style = face.GetLightStyle(slot);

            if (style == BspLightStyles.None)
            {
                break;
            }

            if (style != 0)
            {
                continue;
            }

            var offset = face.LightOffset + (((slot * styleSetCount) + setCount) * setSize);

            if (face.LightOffset < 0 || offset + setSize > lighting.Length)
            {
                return [];
            }

            return lighting.Slice(offset, setSize);
        }

        return [];
    }

    private static void FillWhite(Half[][] pages, LightmapRect rect, int atlasWidth)
    {
        foreach (var page in pages)
        {
            for (var y = rect.Y - Padding; y < rect.Y + rect.Height + Padding; y++)
            {
                for (var x = rect.X - Padding; x < rect.X + rect.Width + Padding; x++)
                {
                    var index = (x + (y * atlasWidth)) * 4;
                    page[index] = Half.One;
                    page[index + 1] = Half.One;
                    page[index + 2] = Half.One;
                    page[index + 3] = Half.Zero;
                }
            }
        }
    }

    // Writes the luxels to the page they were computed for, or to every page when they are shared by all
    private static void WritePadded(Half[][] pages, int page, int computedPages, LightmapRect rect, ReadOnlySpan<Vector4> luxels, int atlasWidth)
    {
        if (computedPages == pages.Length)
        {
            WritePadded(pages[page], rect, luxels, atlasWidth);
            return;
        }

        foreach (var target in pages)
        {
            WritePadded(target, rect, luxels, atlasWidth);
        }
    }

    private static void WritePadded(Half[] page, LightmapRect rect, ReadOnlySpan<Vector4> luxels, int atlasWidth)
    {
        for (var y = -Padding; y < rect.Height + Padding; y++)
        {
            var sourceY = Math.Clamp(y, 0, rect.Height - 1);

            for (var x = -Padding; x < rect.Width + Padding; x++)
            {
                var sourceX = Math.Clamp(x, 0, rect.Width - 1);
                var linear = luxels[sourceX + (sourceY * rect.Width)];
                var index = ((rect.X + x) + ((rect.Y + y) * atlasWidth)) * 4;

                page[index] = (Half)linear.X;
                page[index + 1] = (Half)linear.Y;
                page[index + 2] = (Half)linear.Z;
                page[index + 3] = (Half)linear.W;
            }
        }
    }
}
