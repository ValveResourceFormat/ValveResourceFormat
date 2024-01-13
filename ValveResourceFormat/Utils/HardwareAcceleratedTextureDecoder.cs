using SkiaSharp;
using ValveResourceFormat.ResourceTypes;
using static ValveResourceFormat.ResourceTypes.Texture;

namespace ValveResourceFormat.Utils;

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
    /// <returns>Return false if decode is unsuccessful, will fallback to software decode.</returns>
    public abstract bool Decode(SKBitmap bitmap, Resource resource, int mip, uint depth, CubemapFace face);
}
