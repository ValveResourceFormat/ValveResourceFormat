using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.GoldSrc;

/// <summary>Converts GoldSrc paletted images to in-memory Source 2 textures.</summary>
internal static class GoldSrcTextureConverter
{
    private static readonly byte[] GammaTable = CreateGammaTable();

    /// <summary>Converts an image with all its mip levels to an RGBA texture.</summary>
    /// <param name="image">The image.</param>
    /// <param name="transparentLastColor">Whether the last palette color is drawn transparent.</param>
    /// <param name="generateMips">Whether an image that has only one level gets the rest of its mip chain.</param>
    /// <returns>A resource whose data block is the texture. Its colors are the colors the engine draws, which shaders read as sRGB.</returns>
    public static Resource Convert(PalettedImage image, bool transparentLastColor, bool generateMips = true)
    {
        var mips = CreateMips(image, transparentLastColor, generateMips);

        return Texture.Create((ushort)image.Width, (ushort)image.Height, VTexFormat.RGBA8888, 0, Vector4.Zero, mips);
    }

    /// <summary>Gets whether a decal image takes its opacity from its pixel values. Such a decal is drawn in the last palette color, and its other palette colors are not used.</summary>
    /// <param name="image">The decal image.</param>
    public static bool UsesIndexAsAlpha(PalettedImage image)
    {
        var last = (PalettedImage.PaletteColorCount - 1) * 3;

        return image.Palette[last] != 0 || image.Palette[last + 1] != 0 || image.Palette[last + 2] != 255;
    }

    /// <summary>Converts a decal image that takes its opacity from its pixel values to an RGBA texture.</summary>
    /// <param name="image">The decal image.</param>
    /// <returns>A resource whose data block is the texture. Every texel has the last palette color, and the pixel value is its alpha.</returns>
    public static Resource ConvertIndexAlpha(PalettedImage image)
    {
        var last = (PalettedImage.PaletteColorCount - 1) * 3;
        var red = GammaTable[image.Palette[last]];
        var green = GammaTable[image.Palette[last + 1]];
        var blue = GammaTable[image.Palette[last + 2]];

        var indices = image.Mips[0];
        var rgba = new byte[indices.Length * 4];

        for (var i = 0; i < indices.Length; i++)
        {
            rgba[i * 4] = red;
            rgba[(i * 4) + 1] = green;
            rgba[(i * 4) + 2] = blue;
            rgba[(i * 4) + 3] = indices[i];
        }

        var mips = new List<ReadOnlyMemory<byte>> { rgba };
        AddMipChain(mips, image.Width, image.Height);

        return Texture.Create((ushort)image.Width, (ushort)image.Height, VTexFormat.RGBA8888, 0, Vector4.Zero, mips);
    }

    /// <summary>Converts images of the same size to a texture array with a layer for each image.</summary>
    /// <param name="images">The frames of the animation, which all have the size of the first.</param>
    /// <param name="transparentLastColor">Whether the last palette color is drawn transparent.</param>
    /// <returns>A resource whose data block is the texture array. Its mip levels hold every layer one after another.</returns>
    public static Resource ConvertFrames(IReadOnlyList<PalettedImage> images, bool transparentLastColor)
    {
        var first = images[0];
        var layers = new List<List<ReadOnlyMemory<byte>>>(images.Count);

        foreach (var image in images)
        {
            layers.Add(CreateMips(image, transparentLastColor, generateMips: true));
        }

        var mips = new List<ReadOnlyMemory<byte>>(layers[0].Count);

        for (var level = 0; level < layers[0].Count; level++)
        {
            var levelPixels = new byte[layers.Count * layers[0][level].Length];
            var offset = 0;

            foreach (var layer in layers)
            {
                layer[level].CopyTo(levelPixels.AsMemory(offset));
                offset += layer[level].Length;
            }

            mips.Add(levelPixels);
        }

        return Texture.Create((ushort)first.Width, (ushort)first.Height, VTexFormat.RGBA8888, VTexFlags.TEXTURE_ARRAY, Vector4.Zero, mips, depth: (ushort)images.Count);
    }

    private static List<ReadOnlyMemory<byte>> CreateMips(PalettedImage image, bool transparentLastColor, bool generateMips)
    {
        // A masked texture without a texel of the transparent color is drawn opaque
        var isMasked = transparentLastColor && image.HasTransparentColor;

        // The stored levels of a masked texture drop whole texels, which turns a fine grate into scattered holes
        // with distance. Levels averaged from the base level fade them out instead.
        var storedLevels = isMasked && generateMips ? 1 : image.Mips.Length;
        var mips = new List<ReadOnlyMemory<byte>>(storedLevels);

        for (var i = 0; i < storedLevels; i++)
        {
            var rgba = image.ToRgba(i, isMasked);

            if (isMasked && i == 0)
            {
                PalettedImage.FillTransparentColors(rgba, image.Width, image.Height);
            }

            for (var texel = 0; texel < rgba.Length; texel += 4)
            {
                rgba[texel] = GammaTable[rgba[texel]];
                rgba[texel + 1] = GammaTable[rgba[texel + 1]];
                rgba[texel + 2] = GammaTable[rgba[texel + 2]];
            }

            mips.Add(rgba);
        }

        if (generateMips && mips.Count == 1)
        {
            AddMipChain(mips, image.Width, image.Height);
        }

        return mips;
    }

    /// <summary>Converts the generated texture of a label, such as that of a tools material, to a tiling RGBA texture with all its mip levels.</summary>
    /// <param name="label">The label.</param>
    /// <param name="backgroundOpacity">The alpha of the background. The text is always opaque.</param>
    /// <returns>A resource whose data block is the texture.</returns>
    public static Resource ConvertLabel(ToolsMaterials.TextureLabel label, float backgroundOpacity)
    {
        using var bitmap = MapAutoPhysTextureGenerator.GenerateTexture(label, backgroundOpacity);

        var mips = new List<ReadOnlyMemory<byte>> { bitmap.Bytes };
        AddMipChain(mips, bitmap.Width, bitmap.Height);

        return Texture.Create((ushort)bitmap.Width, (ushort)bitmap.Height, VTexFormat.RGBA8888, 0, label.Background.ToLinearColor(), mips);
    }

    // Each level is the plain average of the 2x2 texels of the one before it, on all four channels
    private static void AddMipChain(List<ReadOnlyMemory<byte>> mips, int width, int height)
    {
        while (width > 1 || height > 1)
        {
            var source = mips[^1].Span;
            var nextWidth = Math.Max(1, width >> 1);
            var nextHeight = Math.Max(1, height >> 1);
            var next = new byte[nextWidth * nextHeight * 4];

            for (var y = 0; y < nextHeight; y++)
            {
                var y0 = Math.Min(y * 2, height - 1);
                var y1 = Math.Min((y * 2) + 1, height - 1);

                for (var x = 0; x < nextWidth; x++)
                {
                    var x0 = Math.Min(x * 2, width - 1);
                    var x1 = Math.Min((x * 2) + 1, width - 1);

                    var topLeft = ((y0 * width) + x0) * 4;
                    var topRight = ((y0 * width) + x1) * 4;
                    var bottomLeft = ((y1 * width) + x0) * 4;
                    var bottomRight = ((y1 * width) + x1) * 4;
                    var target = ((y * nextWidth) + x) * 4;

                    for (var channel = 0; channel < 4; channel++)
                    {
                        var sum = source[topLeft + channel] + source[topRight + channel] + source[bottomLeft + channel] + source[bottomRight + channel];
                        next[target + channel] = (byte)(sum >> 2);
                    }
                }
            }

            mips.Add(next);
            width = nextWidth;
            height = nextHeight;
        }
    }

    private static byte[] CreateGammaTable()
    {
        var table = new byte[256];

        for (var i = 0; i < table.Length; i++)
        {
            var value = MathF.Pow(i / 255f, GoldSrcGamma.TextureExponent);
            table[i] = (byte)MathF.Round(value * 255f);
        }

        return table;
    }
}
