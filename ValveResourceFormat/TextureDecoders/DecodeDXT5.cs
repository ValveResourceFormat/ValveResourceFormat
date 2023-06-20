using System;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeDXT5 : ITextureDecoder
    {
        readonly int w;
        readonly int h;
        readonly bool yCoCg;
        readonly bool normalize;
        readonly bool invert;
        readonly bool hemiOct;

        public DecodeDXT5(int w, int h, bool yCoCg, bool normalize, bool invert, bool hemiOct)
        {
            this.w = w;
            this.h = h;
            this.yCoCg = yCoCg;
            this.normalize = normalize;
            this.invert = invert;
            this.hemiOct = hemiOct;
        }

        public void Decode(SKBitmap imageInfo, Span<byte> input)
        {
            using var pixels = imageInfo.PeekPixels();
            var data = pixels.GetPixelSpan<byte>();
            var offset = 0;
            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;
            var imageWidth = imageInfo.Width;
            var rowBytes = imageInfo.RowBytes;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    var blockAlpha = BitConverter.ToUInt64(input.Slice(offset, 8)); // TODO: Can we work on bytes directly here?
                    offset += 8;
                    var blockStorage = input.Slice(offset, 8);
                    offset += 8;
                    var ofs = (i * 16) + (j * 4 * rowBytes);
                    DecodeDXT1.DecompressBlockDXT1(i * 4, j * 4, imageWidth, blockStorage, data, rowBytes);
                    Decompress8BitBlock(i * 4, imageWidth, ofs + 3, blockAlpha, data, rowBytes);

                    for (var y = 0; y < 4; y++)
                    {
                        for (var x = 0; x < 4; x++)
                        {
                            var dataIndex = ofs + (x * 4) + (y * rowBytes);
                            if ((i * 4) + x >= imageWidth || data.Length < dataIndex + 3)
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
                                    var nx = ((data[dataIndex + 3] + data[dataIndex + 1]) / 255.0f) - 1.003922f;
                                    var ny = (data[dataIndex + 3] - data[dataIndex + 1]) / 255.0f;
                                    var nz = 1 - Math.Abs(nx) - Math.Abs(ny);

                                    var l = MathF.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
                                    data[dataIndex + 3] = data[dataIndex + 2]; //r to alpha
                                    data[dataIndex + 2] = (byte)(((nx / l * 0.5f) + 0.5f) * 255);
                                    data[dataIndex + 1] = (byte)(((ny / l * 0.5f) + 0.5f) * 255);
                                    data[dataIndex + 0] = (byte)(((nz / l * 0.5f) + 0.5f) * 255);
                                }
                                else
                                {
                                    var swizzleA = (data[dataIndex + 3] * 2) - 255;     // premul A
                                    var swizzleG = (data[dataIndex + 1] * 2) - 255;         // premul G
                                    var deriveB = (int)Math.Sqrt((255 * 255) - (swizzleA * swizzleA) - (swizzleG * swizzleG));
                                    data[dataIndex + 2] = ClampColor((swizzleA / 2) + 128); // unpremul A and normalize (128 = forward, or facing viewer)
                                    data[dataIndex + 1] = ClampColor((swizzleG / 2) + 128); // unpremul G and normalize
                                    data[dataIndex + 0] = ClampColor((deriveB / 2) + 128);  // unpremul B and normalize
                                    data[dataIndex + 3] = 255;
                                }
                            }

                            if (invert)
                            {
                                data[dataIndex + 1] = (byte)~data[dataIndex + 1];  // LegacySource1InvertNormals
                            }
                        }
                    }
                }
            }
        }

        internal static void Decompress8BitBlock(int bx, int w, int offset, ulong block, Span<byte> pixels, int stride)
        {
            var e0 = (byte)(block & 0xFF);
            var e1 = (byte)(block >> 8 & 0xFF);
            var code = block >> 16;

            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    var dataIndex = offset + (y * stride) + (x * 4);

                    uint index = (byte)(code & 0x07);
                    code >>= 3;

                    if (bx + x >= w || pixels.Length <= dataIndex)
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
    }
}
