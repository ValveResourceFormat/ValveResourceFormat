using System.IO;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.Compression;

namespace Tests.Compression
{
    public class Z3DecoderTest
    {
        public static IEnumerable<string> FramesWithExpectedOutput()
        {
            foreach (var file in TestFixtures.FilesIn("Z3", "*.z3.bin"))
            {
                var name = file[..^".z3.bin".Length];

                if (File.Exists(TestFixtures.Path("Z3", name + ".raw.bin")))
                {
                    yield return name;
                }
            }
        }

        [Test]
        [MethodDataSource(nameof(FramesWithExpectedOutput))]
        public async Task DecodesFrame(string name)
        {
            var frame = File.ReadAllBytes(TestFixtures.Path("Z3", name + ".z3.bin"));
            var expected = File.ReadAllBytes(TestFixtures.Path("Z3", name + ".raw.bin"));
            var output = new byte[expected.Length];

            var written = Z3Decoder.Decompress(frame, output);

            await Assert.That(written).IsEqualTo(expected.Length);
            await Assert.That(output).IsEquivalentTo(expected, EqualityComparer<byte>.Default, CollectionOrdering.Matching);
        }

        [Test]
        public async Task DecodesMultiBlockFrameWithCrossBlockMatches()
        {
            var frame = File.ReadAllBytes(TestFixtures.Path("Z3", "big_zero_1m_plus.z3.bin"));
            var output = new byte[1048832];
            output.AsSpan().Fill(0xFF);

            var written = Z3Decoder.Decompress(frame, output);

            await Assert.That(written).IsEqualTo(output.Length);
            await Assert.That(output.AsSpan().IndexOfAnyExcept((byte)0)).IsEqualTo(-1);
        }

        [Test]
        public void CorruptedFramesAreDecodedOrRejected()
        {
            var output = new byte[0x10000];

            foreach (var name in TestFixtures.FilesIn("Z3", "*.z3.bin"))
            {
                if (name.StartsWith("big_zero_1m_plus", StringComparison.Ordinal))
                {
                    continue;
                }

                var frame = File.ReadAllBytes(TestFixtures.Path("Z3", name));

                for (var length = 0; length < frame.Length; length++)
                {
                    TryDecompress(frame.AsSpan(0, length), output);
                }

                for (var bit = 0; bit < 8 * frame.Length; bit++)
                {
                    frame[bit >> 3] ^= (byte)(1 << (bit & 7));
                    TryDecompress(frame, output);
                    frame[bit >> 3] ^= (byte)(1 << (bit & 7));
                }
            }
        }

        private static void TryDecompress(ReadOnlySpan<byte> frame, Span<byte> output)
        {
            try
            {
                Z3Decoder.Decompress(frame, output);
            }
            catch (InvalidDataException)
            {
            }
        }

        [Test]
        public void ThrowsOnTruncatedFrame()
        {
            var frame = File.ReadAllBytes(TestFixtures.Path("Z3", "crafted_mode2.z3.bin"));
            var output = new byte[520];

            Assert.Throws<InvalidDataException>(() => Z3Decoder.Decompress(frame.AsSpan(0, frame.Length / 2), output));
        }

        [Test]
        public void ThrowsWhenContentIsLargerThanOutput()
        {
            var frame = File.ReadAllBytes(TestFixtures.Path("Z3", "run_a_300.z3.bin"));
            var output = new byte[299];

            Assert.Throws<InvalidDataException>(() => Z3Decoder.Decompress(frame, output));
        }

        [Test]
        public void ThrowsOnTrailingByte()
        {
            var frame = File.ReadAllBytes(TestFixtures.Path("Z3", "run_a_300.z3.bin"));
            var output = new byte[300];

            Assert.Throws<InvalidDataException>(() => Z3Decoder.Decompress([.. frame, 0], output));
        }

        [Test]
        public void ThrowsOnChecksumMismatch()
        {
            var frame = File.ReadAllBytes(TestFixtures.Path("Z3", "cksum_one_byte.z3.bin"));
            var output = new byte[1];

            frame[^1] ^= 1;

            Assert.Throws<InvalidDataException>(() => Z3Decoder.Decompress(frame, output));
        }

        [Test]
        public void ThrowsOnDictionaryFrame()
        {
            var frame = File.ReadAllBytes(TestFixtures.Path("Z3", "run_a_300.z3.bin"));
            var output = new byte[300];

            frame[0] |= 0x10;

            Assert.Throws<InvalidDataException>(() => Z3Decoder.Decompress(frame, output));
        }
    }
}
