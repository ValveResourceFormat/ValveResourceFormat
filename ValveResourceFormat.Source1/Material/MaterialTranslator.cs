using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using ValveResourceFormat.IO;
using ValveResourceFormat.Utils;
using Channel = ValveResourceFormat.CompiledShader.ChannelMapping;
using VrfTexture = ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Source1.Material;

/// <summary>
/// Writes a described material for a game as a vmat with the images its textures need.
/// </summary>
internal static class MaterialTranslator
{
    private const string VtexExtension = ".vtex";

    // The compiler cannot weigh the mips of a texture by its alpha unless its sides are powers of two
    private const string BoxMipSettings = "\"settings\"\n{\n\t\"mip_algorithm\"\t\t\"Box\"\n}\n";

    public static ContentFile Translate(ResourceTypes.Material material, MaterialDescription description, Game targetGame, Func<string, Resource?> loadFile, ILogger logger)
    {
        var target = MaterialTarget.Get(targetGame)
            ?? throw new NotSupportedException($"Materials cannot be written for {targetGame}.");

        var profile = target.Find(description);

        var translation = MaterialWriter.Write(description, target, (texturePath, suffix, generatedInputName)
            => GetImageName(material, texturePath, suffix, generatedInputName, loadFile));

        foreach (var warning in translation.Warnings)
        {
            logger.LogDebug("{Material}: {Warning}", material.Name, warning);
        }

        if (profile.LdrCubeFeature is { } ldrFeature && translation.Textures.Count > 0)
        {
            var cubeTexture = translation.Textures[0].Source.Texture!;

            if (!IsHighDynamicRange(cubeTexture, loadFile))
            {
                translation.Features[ldrFeature] = 1;
            }
        }

        var vmat = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(translation.ToValveMaterial()),
        };

        foreach (var textures in translation.Textures.GroupBy(texture => texture.Source.Texture))
        {
            var resource = loadFile(textures.Key + GameFileLoader.CompiledFileSuffix);
            if (resource is null)
            {
                continue;
            }

            var maps = textures.DistinctBy(texture => texture.FileName).ToList();
            var vtex = WriteImages(resource, maps, loadFile);

            if (vtex.SubFiles.Count > 0)
            {
                vmat.AdditionalFiles.Add(vtex);
            }
            else
            {
                vtex.Dispose();
            }
        }

        return vmat;
    }

    private static bool IsHighDynamicRange(string texturePath, Func<string, Resource?> loadFile)
    {
        var textureResource = loadFile(texturePath + GameFileLoader.CompiledFileSuffix);

        return (textureResource?.DataBlock as VrfTexture)?.IsHighDynamicRange ?? false;
    }

    private static string GetImageName(ResourceTypes.Material material, string texturePath, string? suffix, string? generatedInputName, Func<string, Resource?> loadFile)
    {
        var isHdr = IsHighDynamicRange(texturePath, loadFile);

        var extension = isHdr ? ".exr" : ".png";
        var stem = texturePath[..^VtexExtension.Length];

        if (suffix is null)
        {
            return stem + extension;
        }

        // An image computed from a texture depends on the material that asked for it, not only on the texture
        if (generatedInputName is not null)
        {
            var hash = StringToken.Get(string.Join('|', material.Name, generatedInputName, texturePath));
            stem += "_" + hash.ToString("x8", CultureInfo.InvariantCulture);
        }

        if (!stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            // A texture that already ends in the suffix would otherwise get the same name
            if (generatedInputName is null)
            {
                var hash = StringToken.Get(texturePath);
                stem += "_" + hash.ToString("x8", CultureInfo.InvariantCulture);
            }

            stem += suffix;
        }

        return stem + extension;
    }

    private static ContentFile WriteImages(Resource resource, List<MaterialTranslation.Texture> maps, Func<string, Resource?> loadFile)
    {
        var texture = (VrfTexture)resource.DataBlock!;
        var extract = new TextureExtract(resource);

        if (texture.Flags.HasFlag(VTexFlags.CUBE_TEXTURE) || texture.Depth > 1 || extract.ExportExr)
        {
            var unpacks = maps.Select(map => new MaterialExtract.UnpackInfo
            {
                TextureType = string.Empty,
                FileName = map.FileName,
                Channel = map.Source.Channel ?? Channel.RGBA,
            });

            return extract.ToMaterialMaps(unpacks);
        }

        var bitmap = texture.GenerateBitmap(decodeFlags: extract.DecodeFlags);
        bitmap.SetImmutable();

        var vtex = new TextureContentFile
        {
            Bitmap = bitmap,
            FileName = resource.FileName!,
        };

        foreach (var map in maps)
        {
            var source = map.Source;
            var second = LoadBitmap(source.SecondTexture, loadFile);
            var imageName = Path.GetFileName(map.FileName);

            vtex.AddImageSubFile(imageName, image => ToPng(image, source, second));

            var (width, height) = GetImageSize(bitmap, source, second);

            if (!BitOperations.IsPow2(width) || !BitOperations.IsPow2(height))
            {
                vtex.AddSubFile(Path.ChangeExtension(imageName, ".txt"), () => Encoding.ASCII.GetBytes(BoxMipSettings));
            }
        }

        return vtex;
    }

    private static SKBitmap? LoadBitmap(string? texturePath, Func<string, Resource?> loadFile)
    {
        if (texturePath is null)
        {
            return null;
        }

        var resource = loadFile(texturePath + GameFileLoader.CompiledFileSuffix);

        return (resource?.DataBlock as VrfTexture)?.GenerateBitmap();
    }

    private static bool IsComputed(MaterialInput source) => source.Function is not null || source.PairFunction is not null;

    // A computed image is as large as the larger of its two textures
    private static (int Width, int Height) GetImageSize(SKBitmap bitmap, MaterialInput source, SKBitmap? second)
    {
        if (!IsComputed(source))
        {
            return (bitmap.Width, bitmap.Height);
        }

        return (Math.Max(bitmap.Width, second?.Width ?? 0), Math.Max(bitmap.Height, second?.Height ?? 0));
    }

    private static byte[] ToPng(SKBitmap bitmap, MaterialInput source, SKBitmap? second)
    {
        if (!IsComputed(source))
        {
            return ToPngChannels(bitmap, source);
        }

        var (width, height) = GetImageSize(bitmap, source, second);

        using var image = new SKBitmap(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var imagePixmap = image.PeekPixels();
        using var pixmap = bitmap.PeekPixels();
        using var secondPixmap = second?.PeekPixels();

        var imagePixels = imagePixmap.GetPixelSpan<byte>();
        var pixels = pixmap.GetPixelSpan<SKColor>();
        ReadOnlySpan<SKColor> secondPixels = [];

        if (secondPixmap is not null)
        {
            secondPixels = secondPixmap.GetPixelSpan<SKColor>();
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = Sample(pixels, bitmap.Width, bitmap.Height, x, y, width, height);
                float value;

                if (source.PairFunction is { } pairFunction)
                {
                    var secondPixel = Sample(secondPixels, second!.Width, second.Height, x, y, width, height);
                    value = pairFunction(pixel, secondPixel);
                }
                else
                {
                    value = source.Function!(pixel);
                }

                imagePixels[(y * width) + x] = (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
            }
        }

        return TextureExtract.ToPngImage(image);
    }

    private static byte[] ToPngChannels(SKBitmap bitmap, MaterialInput source)
    {
        var channel = source.Channel ?? Channel.RGBA;

        if (!source.InvertGreen)
        {
            return TextureExtract.ToPngImageChannels(bitmap, channel);
        }

        using var inverted = bitmap.Copy();
        using var invertedPixmap = inverted.PeekPixels();
        var invertedPixels = invertedPixmap.GetPixelSpan<SKColor>();

        for (var i = 0; i < invertedPixels.Length; i++)
        {
            var pixel = invertedPixels[i];
            invertedPixels[i] = new SKColor(pixel.Red, (byte)(255 - pixel.Green), pixel.Blue, pixel.Alpha);
        }

        return TextureExtract.ToPngImageChannels(inverted, channel);
    }

    private static Vector4 Sample(ReadOnlySpan<SKColor> pixels, int width, int height, int x, int y, int targetWidth, int targetHeight)
    {
        var sourceX = x * width / targetWidth;
        var sourceY = y * height / targetHeight;
        var pixel = pixels[(sourceY * width) + sourceX];

        return new Vector4(pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha) / 255f;
    }
}
