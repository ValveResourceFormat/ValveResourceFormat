using SkiaSharp;
using ValveResourceFormat.ResourceTypes;

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
    public abstract bool Decode(SKBitmap bitmap, Texture texture);
}
