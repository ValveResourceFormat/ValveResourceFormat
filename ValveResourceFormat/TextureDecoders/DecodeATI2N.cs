using System;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeATI2N : ITextureDecoder
    {
        readonly int w;
        readonly int h;
        readonly bool normalize;

        public DecodeATI2N(int w, int h, bool normalize)
        {
            this.w = w;
            this.h = h;
            this.normalize = normalize;
        }

        public void Decode(SKBitmap imageInfo, Span<byte> input)
        {
            using var pixels = imageInfo.PeekPixels();
            var data = pixels.GetPixelSpan<byte>();
            var blockCountX = (w + 3) / 4;
            var blockCountY = (h + 3) / 4;
            var offset = 0;

            for (var j = 0; j < blockCountY; j++)
            {
                for (var i = 0; i < blockCountX; i++)
                {
                    var block1 = BitConverter.ToUInt64(input.Slice(offset, 8));
                    var block2 = BitConverter.ToUInt64(input.Slice(offset + 8, 8));
                    offset += 16;
                    var ofs = ((i * 4) + (j * 4 * w)) * 4;
                    DecodeDXT5.Decompress8BitBlock(i * 4, w, ofs + 2, block1, data, w * 4); //r
                    DecodeDXT5.Decompress8BitBlock(i * 4, w, ofs + 1, block2, data, w * 4); //g

                    for (var y = 0; y < 4; y++)
                    {
                        for (var x = 0; x < 4; x++)
                        {
                            var dataIndex = ofs + ((x + (y * w)) * 4);
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
                                var deriveB = (int)Math.Sqrt((255 * 255) - (swizzleR * swizzleR) - (swizzleG * swizzleG));
                                data[dataIndex + 2] = ClampColor((swizzleR / 2) + 128); // unpremul R and normalize (128 = forward, or facing viewer)
                                data[dataIndex + 1] = ClampColor((swizzleG / 2) + 128); // unpremul G and normalize
                                data[dataIndex + 0] = ClampColor((deriveB / 2) + 128);  // unpremul B and normalize
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
