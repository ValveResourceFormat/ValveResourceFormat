using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeR16 : ITextureDecoder
    {
        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var ushortInput = MemoryMarshal.Cast<byte, ushort>(input);

            if (bitmap.ColorType == SKColorType.RgbaF32)
            {
                var floatOutput = pixels.GetPixelSpan<SKColorF>();
                Debug.Assert(floatOutput.Length == ushortInput.Length);

                for (var i = 0; i < floatOutput.Length; i++)
                {
                    floatOutput[i] = new SKColorF(((float)ushortInput[i]) / ushort.MaxValue, 0f, 0f);
                }
            }
        }
    }
}
