using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ValveResourceFormat.Compression;

namespace Tests.Compression
{
    public class LzmaDecoderTest
    {
        // Magic, uncompressed size and compressed size come before the properties
        private const int BlockHeaderSize = 12;

        private sealed record LzmaBlock(byte[] Properties, byte[] Stream, int UncompressedSize);

        private static LzmaBlock ReadBlock(string fileName)
        {
            var data = File.ReadAllBytes(TestFixtures.Path("Lzma", fileName));
            var uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
            var compressedSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
            var streamStart = BlockHeaderSize + LzmaDecoder.PropertiesSize;

            return new LzmaBlock(data[BlockHeaderSize..streamStart], data[streamStart..(streamStart + compressedSize)], uncompressedSize);
        }

        [Test]
        [Arguments("tiny.lzma", "cb939486b2dfaeca9f7ea0fee583ed61fb7808679a540d962fc019afc9268a6a")]
        [Arguments("larger_than_dictionary.lzma", "ceed1c16f9a8a50fae7c7c91341c0da3ad581cde7fbdcb7519ddce520c5ab7f0")]
        [Arguments("long_matches.lzma", "eb87a49a5efd3aea5ed987f2a894a076bdf58c46aa252783342a430a8f910e8f")]
        public async Task DecodesBlock(string fileName, string expectedSha256)
        {
            using var stream = File.OpenRead(TestFixtures.Path("Lzma", fileName));
            using var reader = new BinaryReader(stream);

            await Assert.That(reader.ReadUInt32()).IsEqualTo(0x414D5A4Cu);

            var uncompressedSize = reader.ReadInt32();
            var compressedSize = reader.ReadInt32();
            var output = new byte[uncompressedSize];

            LzmaDecoder.Decode(stream, compressedSize, output);

            await Assert.That(stream.Position).IsEqualTo(stream.Length);
            await Assert.That(Convert.ToHexStringLower(SHA256.HashData(output))).IsEqualTo(expectedSha256);
        }

        [Test]
        public async Task ThrowsOnTruncatedStream()
        {
            var block = ReadBlock("larger_than_dictionary.lzma");
            var output = new byte[block.UncompressedSize];

            await Assert.That(() => LzmaDecoder.Decode(block.Properties, block.Stream.AsSpan(0, block.Stream.Length - 1), output)).Throws<InvalidDataException>();
        }

        [Test]
        public async Task ThrowsWhenOutputIsLargerThanStream()
        {
            var block = ReadBlock("tiny.lzma");
            var output = new byte[block.UncompressedSize + 1000];

            await Assert.That(() => LzmaDecoder.Decode(block.Properties, block.Stream, output)).Throws<InvalidDataException>();
        }

        [Test]
        public async Task ThrowsOnInvalidProperties()
        {
            var block = ReadBlock("tiny.lzma");
            var output = new byte[block.UncompressedSize];
            byte[] properties = [9 * 5 * 5, 0, 0, 4, 0];

            await Assert.That(() => LzmaDecoder.Decode(properties, block.Stream, output)).Throws<InvalidDataException>();
            await Assert.That(() => LzmaDecoder.Decode(block.Properties.AsSpan(0, 4), block.Stream, output)).Throws<InvalidDataException>();
        }

        [Test]
        public async Task CutsMatchesAtEndOfOutput()
        {
            var block = ReadBlock("long_matches.lzma");
            var expected = new byte[block.UncompressedSize];
            LzmaDecoder.Decode(block.Properties, block.Stream, expected);

            // The data ends in long runs, so these lengths are likely to end inside a match, which has to be cut short rather than rejected
            for (var length = block.UncompressedSize - 64; length < block.UncompressedSize; length++)
            {
                var output = new byte[length];
                LzmaDecoder.Decode(block.Properties, block.Stream, output);

                await Assert.That(output.AsSpan().SequenceEqual(expected.AsSpan(0, length))).IsTrue();
            }
        }

        [Test]
        public async Task ThrowsOnMatchBeforeAnyData()
        {
            var properties = ReadBlock("tiny.lzma").Properties;
            var output = new byte[16];

            // The initial code decodes a match bit followed by a simple match with distance 1
            var match = new byte[32];
            match[1] = 0x7F;
            match[2] = 0xFF;
            match[3] = 0xFD;

            var exception = await Assert.That(() => LzmaDecoder.Decode(properties, match, output)).Throws<InvalidDataException>();
            await Assert.That(exception!.Message).Contains("distance");

            // The initial code decodes a match bit followed by a repeated match bit
            var repeatedMatch = new byte[32];
            repeatedMatch[1] = 0xFF;
            repeatedMatch[2] = 0xFF;
            repeatedMatch[3] = 0xFF;
            repeatedMatch[4] = 0xF0;

            exception = await Assert.That(() => LzmaDecoder.Decode(properties, repeatedMatch, output)).Throws<InvalidDataException>();
            await Assert.That(exception!.Message).Contains("repeated");
        }
    }
}
