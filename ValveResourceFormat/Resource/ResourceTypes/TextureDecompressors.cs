using System;
using System.IO;
using SkiaSharp;

namespace ValveResourceFormat.ResourceTypes
{
    // TODO: Convert these into ITextureDecoder
    internal static class TextureDecompressors
    {
        public static SKBitmap ReadR16(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < span.Length; i++)
            {
                var hr = (byte)(r.ReadUInt16() / 256);

                span[i] = new SKColor(hr, 0, 0, 255);
            }

            return res;
        }

        public static SKBitmap ReadRG1616(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < span.Length; i++)
            {
                var hr = (byte)(r.ReadUInt16() / 256);
                var hg = (byte)(r.ReadUInt16() / 256);

                span[i] = new SKColor(hr, hg, 0, 255);
            }

            return res;
        }

        public static SKBitmap ReadR16F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < span.Length; i++)
            {
                var hr = (byte)((float)r.ReadHalf() * 255);

                span[i] = new SKColor(hr, 0, 0, 255);
            }

            return res;
        }

        public static SKBitmap ReadRG1616F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < span.Length; i++)
            {
                var hr = (byte)((float)r.ReadHalf() * 255);
                var hg = (byte)((float)r.ReadHalf() * 255);

                span[i] = new SKColor(hr, hg, 0, 255);
            }

            return res;
        }

        public static SKBitmap ReadR32F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < span.Length; i++)
            {
                var hr = (byte)(r.ReadSingle() * 255);

                span[i] = new SKColor(hr, 0, 0, 255);
            }

            return res;
        }

        public static SKBitmap ReadRG3232F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < span.Length; i++)
            {
                var hr = (byte)(r.ReadSingle() * 255);
                var hg = (byte)(r.ReadSingle() * 255);

                span[i] = new SKColor(hr, hg, 0, 255);
            }

            return res;
        }

        public static SKBitmap ReadRGB323232F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < span.Length; i++)
            {
                var hr = (byte)(r.ReadSingle() * 255);
                var hg = (byte)(r.ReadSingle() * 255);
                var hb = (byte)(r.ReadSingle() * 255);

                span[i] = new SKColor(hr, hg, hb, 255);
            }

            return res;
        }

        public static SKBitmap ReadRGBA32323232F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var pixels = res.PeekPixels();
            var span = pixels.GetPixelSpan<SKColor>();

            for (var i = 0; i < span.Length; i++)
            {
                var hr = (byte)(r.ReadSingle() * 255);
                var hg = (byte)(r.ReadSingle() * 255);
                var hb = (byte)(r.ReadSingle() * 255);
                var ha = (byte)(r.ReadSingle() * 255);

                span[i] = new SKColor(hr, hg, hb, ha);
            }

            return res;
        }
    }
}
