using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeDXT5 : ITextureDecoder
    {
        readonly int w;
        readonly int h;
        readonly TextureCodec decodeFlags;

        public DecodeDXT5(int w, int h, TextureCodec codec)
        {
            this.w = w;
            this.h = h;
            decodeFlags = codec;
        }

        public void Decode(SKBitmap imageInfo, Span<byte> input)
        {
            using var pixmap = imageInfo.PeekPixels();
            var data = pixmap.GetPixelSpan<byte>();
            var pixels = MemoryMarshal.Cast<byte, Color>(data);

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
                            var pixelIndex = dataIndex / 4;

                            if ((i * 4) + x >= imageWidth || data.Length < dataIndex + 3)
                            {
                                break;
                            }

                            if ((decodeFlags & TextureCodec.YCoCg) != 0)
                            {
                                Common.Undo_YCoCg(ref pixels[pixelIndex]);
                            }

                            // todo: dxt5nm check
                            if ((decodeFlags & TextureCodec.NormalizeNormals) != 0)
                            {
                                var red = pixels[pixelIndex].r;
                                pixels[pixelIndex].r = pixels[pixelIndex].a; //r to alpha
                                pixels[pixelIndex].a = red;

                                if ((decodeFlags & TextureCodec.HemiOctRB) != 0)
                                {
                                    Common.Undo_HemiOct(ref pixels[pixelIndex]);
                                }
                                else
                                {
                                    Common.Undo_NormalizeNormals(ref pixels[pixelIndex]);
                                }
                            }
                        }
                    }
                }
            }
        }

        internal static void Decompress8BitBlock(int bx, int w, int offset, ulong block, Span<byte> data, int stride)
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

                    if (bx + x >= w || data.Length <= dataIndex)
                    {
                        continue;
                    }

                    if (index == 0)
                    {
                        data[dataIndex] = e0;
                    }
                    else if (index == 1)
                    {
                        data[dataIndex] = e1;
                    }
                    else
                    {
                        if (e0 > e1)
                        {
                            data[dataIndex] = (byte)((((8 - index) * e0) + ((index - 1) * e1)) / 7);
                        }
                        else
                        {
                            if (index == 6)
                            {
                                data[dataIndex] = 0;
                            }
                            else if (index == 7)
                            {
                                data[dataIndex] = 255;
                            }
                            else
                            {
                                data[dataIndex] = (byte)((((6 - index) * e0) + ((index - 1) * e1)) / 5);
                            }
                        }
                    }
                }
            }
        }
    }
}
