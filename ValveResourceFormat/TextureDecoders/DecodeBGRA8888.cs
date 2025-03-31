using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeBGRA8888 : ITextureDecoder
    {
        public void Decode(SKBitmap res, Span<byte> input)
        {
            using var pixels = res.PeekPixels();
            var inputPixels = MemoryMarshal.Cast<byte, SKColor>(input);
            var outPixels = pixels.GetPixelSpan<Color32>();

            for (var i = 0; i < outPixels.Length; i++)
            {
                var bgraColor = inputPixels[i];
                outPixels[i] = new Color32(bgraColor.Red, bgraColor.Green, bgraColor.Blue, bgraColor.Alpha);
            }
        }
    }
}
