using System.Threading.Tasks;
using ValveResourceFormat.Utils;

namespace Tests.Utils
{
    public class UniformRandomStreamTest
    {
        [Test]
        [Arguments(38965, new uint[] { 0x3ED2ED85, 0x3F5EC3F3, 0x3F1090EB, 0x3DCA36B1 })]
        [Arguments(-7, new uint[] { 0x3DFB91B4, 0x3EDB606B, 0x3F24CD14, 0x3EEF9005 })]
        [Arguments(0, new uint[] { 0x3ED4FDDE, 0x3DBC5817, 0x3F41A41E, 0x3F079A6F })]
        public async Task RandomFloat(int seed, uint[] expectedBits)
        {
            var random = new UniformRandomStream(seed);

            foreach (var bits in expectedBits)
            {
                await Assert.That(BitConverter.SingleToUInt32Bits(random.RandomFloat())).IsEqualTo(bits);
            }

            random.SetSeed(seed);
            await Assert.That(BitConverter.SingleToUInt32Bits(random.RandomFloat())).IsEqualTo(expectedBits[0]);
        }

        [Test]
        [Arguments(-5, 5, new[] { -4, 2, 4, -3, 5, 5, 4, 1 })]
        [Arguments(0, 1 << 30, new[] { 289723529, 88122087, 882687915, 1040832055 })]
        public async Task RandomInt(int low, int high, int[] expected)
        {
            var random = new UniformRandomStream(1234);

            foreach (var value in expected)
            {
                await Assert.That(random.RandomInt(low, high)).IsEqualTo(value);
            }
        }

        [Test]
        public async Task EmptyIntRangeDoesNotAdvance()
        {
            var random = new UniformRandomStream(1234);

            await Assert.That(random.RandomInt(7, 7)).IsEqualTo(7);
            await Assert.That(random.RandomInt(9, 0)).IsEqualTo(9);
            await Assert.That(BitConverter.SingleToUInt32Bits(random.RandomFloat())).IsEqualTo(0x3F2977CFu);
        }
    }
}
