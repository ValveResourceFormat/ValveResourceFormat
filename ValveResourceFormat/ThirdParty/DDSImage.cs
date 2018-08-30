/*
 * Kons 2012-12-03 Version .1
 *
 * Supported features:
 * - DXT1
 * - DXT5
 * - LinearImage
 *
 * http://code.google.com/p/kprojects/
 * Send me any change/improvement at kons.snok<at>gmail.com
 *
 * License: MIT
 */

using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.ThirdParty
{
    internal static class DDSImage
    {
        public static SKBitmap UncompressDXT1(BinaryReader r, int w, int h, int nw = 0, int nh = 0)
        {
            var imageInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);

            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;

            var data = new byte[imageInfo.RowBytes * h];

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    var blockStorage = r.ReadBytes(8);
                    DecompressBlockDXT1(i * 4, j * 4, w, blockStorage, ref data, imageInfo.RowBytes);
                }
            }

            if (nw > 0 && nh > 0 & w >= nw & h >= nh)
            {
                var powerOfTwoBitmap = CreateBitmap(imageInfo, ref data);
                var sourceBitmap = new SKBitmap(nw, nh);
                var ok = powerOfTwoBitmap.ExtractSubset(sourceBitmap, SKRectI.Create(0, 0, nw, nh));
                if (ok)
                {
                    return sourceBitmap;
                }
                else
                {
                    return powerOfTwoBitmap;
                }
            }

            return CreateBitmap(imageInfo, ref data);
        }

        private static void DecompressBlockDXT1(int x, int y, int width, byte[] blockStorage, ref byte[] pixels, int stride)
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

                    if (x + i < width)
                    {
                        var pixelIndex = ((y + j) * stride) + ((x + i) * 4);
                        pixels[pixelIndex] = finalB;
                        pixels[pixelIndex + 1] = finalG;
                        pixels[pixelIndex + 2] = finalR;
                        pixels[pixelIndex + 3] = byte.MaxValue;
                    }
                }
            }
        }

        public static SKBitmap UncompressDXT5(BinaryReader r, int w, int h, bool yCoCg, int nw = 0, int nh = 0)
        {
            var imageInfo = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;

            var data = new byte[imageInfo.RowBytes * h];

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    var blockStorage = r.ReadBytes(16);
                    DecompressBlockDXT5(i * 4, j * 4, w, blockStorage, ref data, imageInfo.RowBytes, yCoCg);
                }
            }

            if (nw > 0 && nh > 0 & w >= nw & h >= nh)
            {
                var powerOfTwoBitmap = CreateBitmap(imageInfo, ref data);
                var sourceBitmap = new SKBitmap(nw, nh);
                var ok = powerOfTwoBitmap.ExtractSubset(sourceBitmap, SKRectI.Create(0, 0, nw, nh));
                if (ok)
                {
                    return sourceBitmap;
                }
                else
                {
                    return powerOfTwoBitmap;
                }
            }

            return CreateBitmap(imageInfo, ref data);
        }

        private static void DecompressBlockDXT5(int x, int y, int width, byte[] blockStorage, ref byte[] pixels, int stride, bool yCoCg)
        {
            var alpha0 = blockStorage[0];
            var alpha1 = blockStorage[1];

            uint a1 = blockStorage[4];
            var a2 = (uint)blockStorage[5] << 8;
            var a3 = (uint)blockStorage[6] << 16;
            var a4 = (uint)blockStorage[7] << 24;
            var alphaCode1 = a1 | a2 | a3 | a4;

            var alphaCode2 = (ushort)(blockStorage[2] | (blockStorage[3] << 8));

            var color0 = (ushort)(blockStorage[8] | blockStorage[9] << 8);
            var color1 = (ushort)(blockStorage[10] | blockStorage[11] << 8);

            ConvertRgb565ToRgb888(color0, out var r0, out var g0, out var b0);
            ConvertRgb565ToRgb888(color1, out var r1, out var g1, out var b1);

            uint c1 = blockStorage[12];
            var c2 = (uint)blockStorage[13] << 8;
            var c3 = (uint)blockStorage[14] << 16;
            var c4 = (uint)blockStorage[15] << 24;
            var code = c1 | c2 | c3 | c4;

            for (var j = 0; j < 4; j++)
            {
                for (var i = 0; i < 4; i++)
                {
                    var alphaCodeIndex = 3 * ((4 * j) + i);
                    int alphaCode;

                    if (alphaCodeIndex <= 12)
                    {
                        alphaCode = (alphaCode2 >> alphaCodeIndex) & 0x07;
                    }
                    else if (alphaCodeIndex == 15)
                    {
                        alphaCode = (int)(((uint)alphaCode2 >> 15) | ((alphaCode1 << 1) & 0x06));
                    }
                    else
                    {
                        alphaCode = (int)((alphaCode1 >> (alphaCodeIndex - 16)) & 0x07);
                    }

                    byte finalAlpha;
                    if (alphaCode == 0)
                    {
                        finalAlpha = alpha0;
                    }
                    else if (alphaCode == 1)
                    {
                        finalAlpha = alpha1;
                    }
                    else
                    {
                        if (alpha0 > alpha1)
                        {
                            finalAlpha = (byte)((((8 - alphaCode) * alpha0) + ((alphaCode - 1) * alpha1)) / 7);
                        }
                        else
                        {
                            if (alphaCode == 6)
                            {
                                finalAlpha = 0;
                            }
                            else if (alphaCode == 7)
                            {
                                finalAlpha = 255;
                            }
                            else
                            {
                                finalAlpha = (byte)((((6 - alphaCode) * alpha0) + ((alphaCode - 1) * alpha1)) / 5);
                            }
                        }
                    }

                    var colorCode = (byte)((code >> (2 * ((4 * j) + i))) & 0x03);

                    byte finalR = 0, finalG = 0, finalB = 0;

                    switch (colorCode)
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
                            finalR = (byte)(((2 * r0) + r1) / 3);
                            finalG = (byte)(((2 * g0) + g1) / 3);
                            finalB = (byte)(((2 * b0) + b1) / 3);
                            break;
                        case 3:
                            finalR = (byte)(((2 * r1) + r0) / 3);
                            finalG = (byte)(((2 * g1) + g0) / 3);
                            finalB = (byte)(((2 * b1) + b0) / 3);
                            break;
                    }

                    if (x + i < width)
                    {
                        if (yCoCg)
                        {
                            var s = (finalB >> 3) + 1;
                            var co = (finalR - 128) / s;
                            var cg = (finalG - 128) / s;

                            finalR = ClampColor(finalAlpha + co - cg);
                            finalG = ClampColor(finalAlpha + cg);
                            finalB = ClampColor(finalAlpha - co - cg);
                        }

                        var pixelIndex = ((y + j) * stride) + ((x + i) * 4);
                        pixels[pixelIndex] = finalB;
                        pixels[pixelIndex + 1] = finalG;
                        pixels[pixelIndex + 2] = finalR;
                        pixels[pixelIndex + 3] = byte.MaxValue; // TODO: Where's my alpha at?
                    }
                }
            }
        }

        private static SKBitmap CreateBitmap(SKImageInfo imageInfo, ref byte[] data)
        {
            // pin the managed array so that the GC doesn't move it
            var gcHandle = GCHandle.Alloc(data, GCHandleType.Pinned);

            // install the pixels with the color type of the pixel data
            var bitmap = new SKBitmap();
            bitmap.InstallPixels(imageInfo, gcHandle.AddrOfPinnedObject(), imageInfo.RowBytes, null, delegate { gcHandle.Free(); }, null);

            return bitmap;
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
