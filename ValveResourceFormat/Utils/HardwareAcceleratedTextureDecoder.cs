using SkiaSharp;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;
using static ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Utils;

#nullable disable

public static class HardwareAcceleratedTextureDecoder
{
    /// <summary>
    /// For use in applications that can decode a <see cref="Texture"/> using a GPU.
    /// </summary>
    public static IHardwareTextureDecoder Decoder { get; set; }
}

public interface IHardwareTextureDecoder
{
    /// <summary>
    /// Decode a texture.
    /// </summary>
    /// <param name="bitmap">The bitmap to put the result into.</param>
    /// <param name="resource">The texture resource to decode.</param>
    /// <param name="depth">The depth to extract.</param>
    /// <param name="face">The face to extract for cube textures.</param>
    /// <param name="mipLevel">The mip level to extract.</param>
    /// <returns>Return false if decode is unsuccessful, will fallback to software decode.</returns>
    public abstract bool Decode(SKBitmap bitmap, Resource resource, uint depth, CubemapFace face, uint mipLevel, TextureCodec decodeFlags);
}
