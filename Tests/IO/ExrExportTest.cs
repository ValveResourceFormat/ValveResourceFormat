using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests.IO
{
    public class ExrExportTest
    {
        private const int Magic = 20000630;
        private const int HeaderStart = 8;
        private const int LinesPerBlock = 16;
        private const int ChannelCount = 4;

        // One-letter name and terminator, then pixel type, pLinear, reserved bytes and sampling
        private const int ChannelEntrySize = 2 + 16;

        // Channels are stored as A, B, G, R
        private static readonly int[] RgbaIndexOfChannel = [3, 2, 1, 0];

        [Test]
        public async Task HdrTextureExportsWellFormedExr()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestFixtures.Path("Textures"), "R32F.vtex_c"));

            var texture = (Texture?)resource.DataBlock;
            Debug.Assert(texture != null);

            using var bitmap = texture.GenerateBitmap();
            var exr = TextureExtract.ToExrImage(bitmap);

            var header = ReadHeader(exr, out _);

            using (Assert.Multiple())
            {
                await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(exr)).IsEqualTo(Magic);
                await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(exr.AsSpan(4))).IsEqualTo(2);

                // A, B, G, R, each FLOAT, linear, sampled at every pixel
                await Assert.That(header["channels"].Length).IsEqualTo(ChannelCount * ChannelEntrySize + 1);
                await Assert.That(Encoding.ASCII.GetString(header["channels"].AsSpan(0, 2))).IsEqualTo("A\0");
                await Assert.That(header["compression"]).IsEquivalentTo(new byte[] { 3 }, EqualityComparer<byte>.Default);

                var window = header["dataWindow"];
                await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(window.AsSpan(8))).IsEqualTo(bitmap.Width - 1);
                await Assert.That(BinaryPrimitives.ReadInt32LittleEndian(window.AsSpan(12))).IsEqualTo(bitmap.Height - 1);
            }
        }

        [Test]
        public async Task ExrRoundTripsPixelsAcrossMultipleBlocks()
        {
            const int Width = 37;
            const int Height = 40;

            using var bitmap = new SKBitmap(Width, Height, SKColorType.RgbaF32, SKAlphaType.Unpremul);
            var expected = FillPixels(bitmap);

            var exr = TextureExtract.ToExrImage(bitmap);
            var decoded = Decode(exr, Width, Height);

            await Assert.That(decoded).IsEquivalentTo(expected, EqualityComparer<float>.Default);
        }

        private static float[] FillPixels(SKBitmap bitmap)
        {
            using var pixels = bitmap.PeekPixels();
            var floats = MemoryMarshal.Cast<SKColorF, float>(pixels.GetPixelSpan<SKColorF>());
            var random = new Random(1234);

            for (var i = 0; i < floats.Length; i++)
            {
                // Values above 1 matter for HDR, repeated values make some blocks compressible
                floats[i] = i % 3 == 0 ? 0.25f : random.NextSingle() * 100f;
            }

            return floats.ToArray();
        }

        private static Dictionary<string, byte[]> ReadHeader(byte[] exr, out int headerEnd)
        {
            var attributes = new Dictionary<string, byte[]>();
            var position = HeaderStart;

            while (exr[position] != 0)
            {
                var name = ReadNullTerminated(exr, ref position);
                ReadNullTerminated(exr, ref position);

                var size = BinaryPrimitives.ReadInt32LittleEndian(exr.AsSpan(position));
                position += sizeof(int);

                attributes[name] = exr.AsSpan(position, size).ToArray();
                position += size;
            }

            headerEnd = position + 1;
            return attributes;
        }

        private static string ReadNullTerminated(byte[] data, ref int position)
        {
            var length = Array.IndexOf(data, (byte)0, position) - position;
            var text = Encoding.ASCII.GetString(data, position, length);
            position += length + 1;
            return text;
        }

        // Returns RGBA pixels, undoing the channel order, byte split and delta predictor of the file
        private static float[] Decode(byte[] exr, int width, int height)
        {
            ReadHeader(exr, out var tableStart);

            var result = new float[width * height * ChannelCount];
            var blockCount = (height + LinesPerBlock - 1) / LinesPerBlock;

            for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                var blockStart = (int)BinaryPrimitives.ReadInt64LittleEndian(exr.AsSpan(tableStart + blockIndex * sizeof(long)));
                var firstLine = BinaryPrimitives.ReadInt32LittleEndian(exr.AsSpan(blockStart));
                var dataSize = BinaryPrimitives.ReadInt32LittleEndian(exr.AsSpan(blockStart + sizeof(int)));
                var data = exr.AsSpan(blockStart + 2 * sizeof(int), dataSize);

                var lineCount = Math.Min(LinesPerBlock, height - firstLine);
                var rawSize = lineCount * ChannelCount * width * sizeof(float);
                var raw = new byte[rawSize];

                if (dataSize == rawSize)
                {
                    data.CopyTo(raw);
                }
                else
                {
                    using var input = new MemoryStream(data.ToArray());
                    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                    var reordered = new byte[rawSize];
                    zlib.ReadExactly(reordered);

                    for (var i = 1; i < rawSize; i++)
                    {
                        reordered[i] = (byte)(reordered[i - 1] + reordered[i] - 128);
                    }

                    var half = rawSize / 2;
                    for (var i = 0; i < half; i++)
                    {
                        raw[2 * i] = reordered[i];
                        raw[2 * i + 1] = reordered[half + i];
                    }
                }

                var position = 0;

                for (var line = 0; line < lineCount; line++)
                {
                    foreach (var rgbaIndex in RgbaIndexOfChannel)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var value = BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(position));
                            position += sizeof(float);
                            result[((firstLine + line) * width + x) * ChannelCount + rgbaIndex] = value;
                        }
                    }
                }
            }

            return result;
        }
    }
}
