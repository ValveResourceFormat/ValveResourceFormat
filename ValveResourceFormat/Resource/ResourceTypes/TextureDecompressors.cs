using System;
using System.IO;
using SkiaSharp;

namespace ValveResourceFormat.ResourceTypes
{
    internal static class TextureDecompressors
    {
        public static SKBitmap ReadI8(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var color = r.ReadByte();

                    res.SetPixel(x, y, new SKColor(color, color, color, 255));
                }
            }

            return res;
        }

        public static SKBitmap ReadIA88(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var color = r.ReadByte();
                    var alpha = r.ReadByte();

                    res.SetPixel(x, y, new SKColor(color, color, color, alpha));
                }
            }

            return res;
        }

        public static SKBitmap ReadUIntPixels(BinaryReader r, int w, int h, SKColorType colorType)
        {
            var res = new SKBitmap(w, h, colorType, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    res.SetPixel(x, y, new SKColor(r.ReadUInt32()));
                }
            }

            return res;
        }

        public static SKBitmap ReadR16(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var hr = (byte)(r.ReadUInt16() / 256);

                    res.SetPixel(x, y, new SKColor(hr, 0, 0, 255));
                }
            }

            return res;
        }

        public static SKBitmap ReadRG1616(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var hr = (byte)(r.ReadUInt16() / 256);
                    var hg = (byte)(r.ReadUInt16() / 256);

                    res.SetPixel(x, y, new SKColor(hr, hg, 0, 255));
                }
            }

            return res;
        }

        public static SKBitmap ReadR16F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var hr = (byte)(HalfTypeHelper.Convert(r.ReadUInt16()) * 255);

                    res.SetPixel(x, y, new SKColor(hr, 0, 0, 255));
                }
            }

            return res;
        }

        public static SKBitmap ReadRG1616F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var hr = (byte)(HalfTypeHelper.Convert(r.ReadUInt16()) * 255);
                    var hg = (byte)(HalfTypeHelper.Convert(r.ReadUInt16()) * 255);

                    res.SetPixel(x, y, new SKColor(hr, hg, 0, 255));
                }
            }

            return res;
        }

        public static void ReadRGBA16161616(SKImageInfo imageInfo, BinaryReader r, Span<byte> data)
        {
            var bytes = r.ReadBytes(imageInfo.Width * imageInfo.Height * 8);
            var log = 0d;

            for (int i = 0, j = 0; i < bytes.Length; i += 8, j += 4)
            {
                var hr = BitConverter.ToUInt16(bytes, i + 0) / 256f;
                var hg = BitConverter.ToUInt16(bytes, i + 2) / 256f;
                var hb = BitConverter.ToUInt16(bytes, i + 4) / 256f;
                var lum = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                log += Math.Log(0.0000000001d + lum);
            }

            log = Math.Exp(log / (imageInfo.Width * imageInfo.Height));

            for (int i = 0, j = 0; i < bytes.Length; i += 8, j += 4)
            {
                var hr = BitConverter.ToUInt16(bytes, i + 0) / 256f;
                var hg = BitConverter.ToUInt16(bytes, i + 2) / 256f;
                var hb = BitConverter.ToUInt16(bytes, i + 4) / 256f;
                var ha = BitConverter.ToUInt16(bytes, i + 6) / 256f;

                var y = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                var u = (hb - y) * 0.565f;
                var v = (hr - y) * 0.713f;

                var mul = 4.0f * y / log;
                mul = mul / (1.0f + mul);
                mul /= y;

                hr = (float)Math.Pow((y + (1.403f * v)) * mul, 2.25f);
                hg = (float)Math.Pow((y - (0.344f * u) - (0.714f * v)) * mul, 2.25f);
                hb = (float)Math.Pow((y + (1.770f * u)) * mul, 2.25f);

#pragma warning disable SA1503
                if (hr < 0) hr = 0;
                if (hr > 1) hr = 1;
                if (hg < 0) hg = 0;
                if (hg > 1) hg = 1;
                if (hb < 0) hb = 0;
                if (hb > 1) hb = 1;
#pragma warning restore SA1503

                data[j + 0] = (byte)(hr * 255); // r
                data[j + 1] = (byte)(hg * 255); // g
                data[j + 2] = (byte)(hb * 255); // b
                data[j + 3] = (byte)(ha * 255); // a
            }
        }

        public static void ReadRGBA16161616F(SKImageInfo imageInfo, BinaryReader r, Span<byte> data)
        {
            var bytes = r.ReadBytes(imageInfo.Width * imageInfo.Height * 8);
            var log = 0d;

            for (int i = 0, j = 0; i < bytes.Length; i += 8, j += 4)
            {
                var hr = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 0));
                var hg = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 2));
                var hb = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 4));
                var lum = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                log += Math.Log(0.0000000001d + lum);
            }

            log = Math.Exp(log / (imageInfo.Width * imageInfo.Height));

            for (int i = 0, j = 0; i < bytes.Length; i += 8, j += 4)
            {
                var hr = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 0));
                var hg = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 2));
                var hb = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 4));
                var ha = HalfTypeHelper.Convert(BitConverter.ToUInt16(bytes, i + 6));

                var y = (hr * 0.299f) + (hg * 0.587f) + (hb * 0.114f);
                var u = (hb - y) * 0.565f;
                var v = (hr - y) * 0.713f;

                var mul = 4.0f * y / log;
                mul = mul / (1.0f + mul);
                mul /= y;

                hr = (float)Math.Pow((y + (1.403f * v)) * mul, 2.25f);
                hg = (float)Math.Pow((y - (0.344f * u) - (0.714f * v)) * mul, 2.25f);
                hb = (float)Math.Pow((y + (1.770f * u)) * mul, 2.25f);

#pragma warning disable SA1503
                if (hr < 0) hr = 0;
                if (hr > 1) hr = 1;
                if (hg < 0) hg = 0;
                if (hg > 1) hg = 1;
                if (hb < 0) hb = 0;
                if (hb > 1) hb = 1;
#pragma warning restore SA1503

                data[j + 0] = (byte)(hr * 255); // r
                data[j + 1] = (byte)(hg * 255); // g
                data[j + 2] = (byte)(hb * 255); // b
                data[j + 3] = (byte)(ha * 255); // a
            }
        }

        public static SKBitmap ReadR32F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var hr = (byte)(r.ReadSingle() * 255);

                    res.SetPixel(x, y, new SKColor(hr, 0, 0, 255));
                }
            }

            return res;
        }

        public static SKBitmap ReadRG3232F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var hr = (byte)(r.ReadSingle() * 255);
                    var hg = (byte)(r.ReadSingle() * 255);

                    res.SetPixel(x, y, new SKColor(hr, hg, 0, 255));
                }
            }

            return res;
        }

        public static SKBitmap ReadRGB323232F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var hr = (byte)(r.ReadSingle() * 255);
                    var hg = (byte)(r.ReadSingle() * 255);
                    var hb = (byte)(r.ReadSingle() * 255);

                    res.SetPixel(x, y, new SKColor(hr, hg, hb, 255));
                }
            }

            return res;
        }

        public static SKBitmap ReadRGBA32323232F(BinaryReader r, int w, int h)
        {
            var res = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var hr = (byte)(r.ReadSingle() * 255);
                    var hg = (byte)(r.ReadSingle() * 255);
                    var hb = (byte)(r.ReadSingle() * 255);
                    var ha = (byte)(r.ReadSingle() * 255);

                    res.SetPixel(x, y, new SKColor(hr, hg, hb, ha));
                }
            }

            return res;
        }

        public static void UncompressATI1N(BinaryReader r, Span<byte> data, int w, int h)
        {
            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    ulong block1 = r.ReadUInt64();
                    int ofs = ((i * 4) + (j * 4 * w)) * 4;
                    Decompress8BitBlock(i * 4, w, ofs, block1, data, w * 4);

                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            int dataIndex = ofs + ((x + (y * w)) * 4);
                            if (data.Length < dataIndex + 3)
                            {
                                break;
                            }

                            data[dataIndex + 1] = data[dataIndex];
                            data[dataIndex + 2] = data[dataIndex];
                            data[dataIndex + 3] = byte.MaxValue;
                        }
                    }
                }
            }
        }

        public static void UncompressATI2N(BinaryReader r, Span<byte> data, int w, int h, bool normalize)
        {
            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    ulong block1 = r.ReadUInt64();
                    ulong block2 = r.ReadUInt64();
                    int ofs = ((i * 4) + (j * 4 * w)) * 4;
                    Decompress8BitBlock(i * 4, w, ofs + 2, block1, data, w * 4); //r
                    Decompress8BitBlock(i * 4, w, ofs + 1, block2, data, w * 4); //g
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            int dataIndex = ofs + ((x + (y * w)) * 4);
                            if (data.Length < dataIndex + 3)
                            {
                                break;
                            }

                            data[dataIndex + 0] = 0; //b
                            data[dataIndex + 3] = byte.MaxValue;
                            if (normalize)
                            {
                                var swizzleR = (data[dataIndex + 2] * 2) - 255;     // premul R
                                var swizzleG = (data[dataIndex + 1] * 2) - 255;     // premul G
                                var deriveB = (int)System.Math.Sqrt((255 * 255) - (swizzleR * swizzleR) - (swizzleG * swizzleG));
                                data[dataIndex + 2] = ClampColor((swizzleR / 2) + 128); // unpremul R and normalize (128 = forward, or facing viewer)
                                data[dataIndex + 1] = ClampColor((swizzleG / 2) + 128); // unpremul G and normalize
                                data[dataIndex + 0] = ClampColor((deriveB / 2) + 128);  // unpremul B and normalize
                            }
                        }
                    }
                }
            }
        }

        public static void UncompressDXT1(SKImageInfo imageInfo, BinaryReader r, Span<byte> data, int w, int h)
        {
            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    var blockStorage = r.ReadBytes(8);
                    DecompressBlockDXT1(i * 4, j * 4, imageInfo.Width, blockStorage, data, imageInfo.RowBytes);
                }
            }
        }

        private static void DecompressBlockDXT1(int x, int y, int width, byte[] blockStorage, Span<byte> pixels, int stride)
        {
            var color0 = (ushort)(blockStorage[0] | blockStorage[1] << 8);
            var color1 = (ushort)(blockStorage[2] | blockStorage[3] << 8);

            ConvertRgb565ToRgb888(color0, out var r0, out var g0, out var b0);
            ConvertRgb565ToRgb888(color1, out var r1, out var g1, out var b1);

            uint c1 = blockStorage[4];
            var c2 = (uint)blockStorage[5] << 8;
            var c3 = (uint)blockStorage[6] << 16;
            var c4 = (uint)blockStorage[7] << 24;
            var code = c1 | c2 | c3 | c4;

            for (var j = 0; j < 4; j++)
            {
                for (var i = 0; i < 4; i++)
                {
                    var positionCode = (byte)((code >> (2 * ((4 * j) + i))) & 0x03);

                    byte finalR = 0, finalG = 0, finalB = 0;

                    switch (positionCode)
                    {
                        case 0:
                            finalR = r0;
                            finalG = g0;
                            finalB = b0;
                            break;
                        case 1:
                            finalR = r1;
                            finalG = g1;
                            finalB = b1;
                            break;
                        case 2:
                            if (color0 > color1)
                            {
                                finalR = (byte)(((2 * r0) + r1) / 3);
                                finalG = (byte)(((2 * g0) + g1) / 3);
                                finalB = (byte)(((2 * b0) + b1) / 3);
                            }
                            else
                            {
                                finalR = (byte)((r0 + r1) / 2);
                                finalG = (byte)((g0 + g1) / 2);
                                finalB = (byte)((b0 + b1) / 2);
                            }

                            break;
                        case 3:
                            if (color0 < color1)
                            {
                                break;
                            }

                            finalR = (byte)(((2 * r1) + r0) / 3);
                            finalG = (byte)(((2 * g1) + g0) / 3);
                            finalB = (byte)(((2 * b1) + b0) / 3);
                            break;
                    }

                    var pixelIndex = ((y + j) * stride) + ((x + i) * 4);

                    if (x + i < width && pixels.Length > pixelIndex + 3)
                    {
                        pixels[pixelIndex] = finalB;
                        pixels[pixelIndex + 1] = finalG;
                        pixels[pixelIndex + 2] = finalR;
                        pixels[pixelIndex + 3] = byte.MaxValue;
                    }
                }
            }
        }

        public static void UncompressDXT5(SKImageInfo imageInfo, BinaryReader r, Span<byte> data, int w, int h, bool yCoCg, bool normalize, bool invert, bool hemiOct)
        {
            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    ulong blockAlpha = r.ReadUInt64();
                    var blockStorage = r.ReadBytes(8);
                    int ofs = (i * 16) + (j * 4 * imageInfo.RowBytes);
                    DecompressBlockDXT1(i * 4, j * 4, imageInfo.Width, blockStorage, data, imageInfo.RowBytes);
                    Decompress8BitBlock(i * 4, imageInfo.Width, ofs + 3, blockAlpha, data, imageInfo.RowBytes);

                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            int dataIndex = ofs + ((x * 4) + (y * imageInfo.RowBytes));
                            if ((i * 4) + x >= imageInfo.Width || data.Length < dataIndex + 3)
                            {
                                break;
                            }

                            if (yCoCg)
                            {
                                var s = (data[dataIndex + 0] >> 3) + 1;
                                var co = (data[dataIndex + 2] - 128) / s;
                                var cg = (data[dataIndex + 1] - 128) / s;

                                data[dataIndex + 2] = ClampColor(data[dataIndex + 3] + co - cg);
                                data[dataIndex + 1] = ClampColor(data[dataIndex + 3] + cg);
                                data[dataIndex + 0] = ClampColor(data[dataIndex + 3] - co - cg);
                                data[dataIndex + 3] = 255; // TODO: yCoCg should have an alpha too?
                            }

                            if (normalize)
                            {
                                if (hemiOct)
                                {
                                    float SignNotZero(float v)
                                    {
                                        return (v >= 0.0f) ? +1.0f : -1.0f;
                                    }

                                    float nx = ((data[dataIndex + 3] / 255.0f) * 2) - 1;
                                    float ny = ((data[dataIndex + 1] / 255.0f) * 2) - 1;
                                    float nz = 1 - Math.Abs(nx) - Math.Abs(ny);
                                    if (nz < 0)
                                    {
                                        float t = (1 - Math.Abs(ny)) * SignNotZero(nx);
                                        ny = (1 - Math.Abs(nx)) * SignNotZero(ny);
                                        nx = t;
                                    }

                                    float l = (float)Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
                                    data[dataIndex + 3] = data[dataIndex + 2]; //r to alpha
                                    data[dataIndex + 2] = (byte)(((nx / l * 0.5f) + 0.5f) * 255);
                                    data[dataIndex + 1] = (byte)(((ny / l * 0.5f) + 0.5f) * 255);
                                    data[dataIndex + 0] = (byte)(((nz / l * 0.5f) + 0.5f) * 255);
                                }
                                else
                                {
                                    var swizzleA = (data[dataIndex + 3] * 2) - 255;     // premul A
                                    var swizzleG = (data[dataIndex + 1] * 2) - 255;         // premul G
                                    var deriveB = (int)System.Math.Sqrt((255 * 255) - (swizzleA * swizzleA) - (swizzleG * swizzleG));
                                    data[dataIndex + 2] = ClampColor((swizzleA / 2) + 128); // unpremul A and normalize (128 = forward, or facing viewer)
                                    data[dataIndex + 1] = ClampColor((swizzleG / 2) + 128); // unpremul G and normalize
                                    data[dataIndex + 0] = ClampColor((deriveB / 2) + 128);  // unpremul B and normalize
                                    data[dataIndex + 3] = 255;
                                }
                            }

                            if (invert)
                            {
                                data[dataIndex + 1] = (byte)(~data[dataIndex + 1]);  // LegacySource1InvertNormals
                            }
                        }
                    }
                }
            }
        }

        private static void Decompress8BitBlock(int bx, int w, int offset, ulong block, Span<byte> pixels, int stride)
        {
            byte e0 = (byte)(block & 0xFF);
            byte e1 = (byte)(block >> 8 & 0xFF);
            ulong code = block >> 16;

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    var dataIndex = offset + (y * stride) + (x * 4);

                    uint index = (byte)(code & 0x07);
                    code >>= 3;

                    if (bx + x > w || pixels.Length <= dataIndex)
                    {
                        continue;
                    }

                    if (index == 0)
                    {
                        pixels[dataIndex] = e0;
                    }
                    else if (index == 1)
                    {
                        pixels[dataIndex] = e1;
                    }
                    else
                    {
                        if (e0 > e1)
                        {
                            pixels[dataIndex] = (byte)((((8 - index) * e0) + ((index - 1) * e1)) / 7);
                        }
                        else
                        {
                            if (index == 6)
                            {
                                pixels[dataIndex] = 0;
                            }
                            else if (index == 7)
                            {
                                pixels[dataIndex] = 255;
                            }
                            else
                            {
                                pixels[dataIndex] = (byte)((((6 - index) * e0) + ((index - 1) * e1)) / 5);
                            }
                        }
                    }
                }
            }
        }

        private static byte ClampColor(int a)
        {
            if (a > 255)
            {
                return 255;
            }

            return a < 0 ? (byte)0 : (byte)a;
        }

        private static void ConvertRgb565ToRgb888(ushort color, out byte r, out byte g, out byte b)
        {
            int temp;

            temp = ((color >> 11) * 255) + 16;
            r = (byte)(((temp / 32) + temp) / 32);
            temp = (((color & 0x07E0) >> 5) * 255) + 32;
            g = (byte)(((temp / 64) + temp) / 64);
            temp = ((color & 0x001F) * 255) + 16;
            b = (byte)(((temp / 32) + temp) / 32);
        }
    }
}
