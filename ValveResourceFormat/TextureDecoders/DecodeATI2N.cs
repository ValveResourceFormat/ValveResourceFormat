using System.Runtime.InteropServices;
using SkiaSharp;

namespace ValveResourceFormat.TextureDecoders
{
    internal class DecodeATI2N : ITextureDecoder
    {
        readonly int w;
        readonly int h;
        readonly TextureCodec decodeFlags;

        public DecodeATI2N(int w, int h, TextureCodec codec)
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
                            var pixelIndex = dataIndex / 4;

                            if (data.Length < dataIndex + 3)
                            {
                                break;
                            }

                            pixels[pixelIndex].b = 0; //b
                            pixels[pixelIndex].a = byte.MaxValue;

                            if ((decodeFlags & TextureCodec.NormalizeNormals) != 0)
                            {
                                Common.Undo_NormalizeNormals(ref pixels[pixelIndex]);
                            }

                            if ((decodeFlags & TextureCodec.HemiOctRB) != 0)
                            {
                                Common.Undo_HemiOct(ref pixels[pixelIndex]);
                            }
                        }
                    }
                }
            }
        }
    }
}
