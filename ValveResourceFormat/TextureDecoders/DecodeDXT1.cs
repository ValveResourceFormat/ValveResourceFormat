using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeDXT1 : ITextureDecoder
    {
        readonly int blockCountX;
        readonly int blockCountY;

        public DecodeDXT1(int width, int height)
        {
            blockCountX = (width + 3) / 4;
            blockCountY = (height + 3) / 4;
        }

        public void Decode(SKBitmap bitmap, Span<byte> input)
        {
            using var pixels = bitmap.PeekPixels();
            var data = pixels.GetPixelSpan<byte>();
            var offset = 0;
            var imageWidth = bitmap.Width;
            var rowBytes = bitmap.RowBytes;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    var blockStorage = input.Slice(offset, 8);
                    offset += 8;
                    DecompressBlockDXT1(i * 4, j * 4, imageWidth, blockStorage, data, rowBytes);
                }
            }
        }

        internal static void DecompressBlockDXT1(int x, int y, int width, Span<byte> blockStorage, Span<byte> data, int stride)
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
                            if (color0 <= color1)
                            {
                                break;
                            }

                            finalR = (byte)(((2 * r1) + r0) / 3);
                            finalG = (byte)(((2 * g1) + g0) / 3);
                            finalB = (byte)(((2 * b1) + b0) / 3);
                            break;
                    }

                    var pixelIndex = ((y + j) * stride) + ((x + i) * 4);

                    if (x + i < width && data.Length > pixelIndex + 3)
                    {
                        data[pixelIndex] = finalB;
                        data[pixelIndex + 1] = finalG;
                        data[pixelIndex + 2] = finalR;
                        data[pixelIndex + 3] = byte.MaxValue;
                    }
                }
            }
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
