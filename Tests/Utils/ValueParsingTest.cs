using System.Threading.Tasks;
using ValveResourceFormat.Utils;

namespace Tests.Utils
{
    public class ValueParsingTest
    {
        [Test]
        public async Task ParseFloatAcceptsWholeNumbersOnly()
        {
            await Assert.That(ValueParsing.ParseFloat(" \t+1.5e1 ")).IsEqualTo(15f);

            string[] rejected = ["", "+", "1,5", "1 2", "0x10", "1e", "--5", "1.5abc"];

            foreach (var text in rejected)
            {
                await Assert.That(ValueParsing.TryParseFloat(text, out _)).IsFalse().Because($"\"{text}\"");
            }
        }

        [Test]
        public async Task ParseFloatReadsInfinityAndNan()
        {
            await Assert.That(ValueParsing.ParseFloat("-Infinity")).IsEqualTo(float.NegativeInfinity);
            await Assert.That(float.IsNaN(ValueParsing.ParseFloat("NaN"))).IsTrue();
        }

        [Test]
        public async Task ParseFloatSaturatesOutOfRange()
        {
            await Assert.That(ValueParsing.ParseFloat("1e999")).IsEqualTo(float.PositiveInfinity);
            await Assert.That(ValueParsing.ParseFloat("-1e999")).IsEqualTo(float.NegativeInfinity);
            await Assert.That(ValueParsing.TryParseFloat("1e-999", out var tiny)).IsTrue();
            await Assert.That(tiny).IsEqualTo(0f);
            await Assert.That(ValueParsing.ParseFloat("+-5")).IsEqualTo(-5f);
        }

        [Test]
        public async Task ParseBoolFollowsEngineRules()
        {
            await Assert.That(ValueParsing.ParseBool("Yes")).IsTrue();
            await Assert.That(ValueParsing.ParseBool("No", true)).IsFalse();
            await Assert.That(ValueParsing.ParseBool("-5")).IsTrue();
            await Assert.That(ValueParsing.ParseBool("1.0", true)).IsTrue();
            await Assert.That(ValueParsing.ParseBool("1.0")).IsTrue();
            await Assert.That(ValueParsing.ParseBool("0.0", true)).IsFalse();
            await Assert.That(ValueParsing.ParseBool("abc", true)).IsTrue();
            await Assert.That(ValueParsing.ParseBool("", true)).IsFalse();
            await Assert.That(ValueParsing.ParseBool(" 1")).IsTrue();
            await Assert.That(ValueParsing.ParseBool("1 ", true)).IsTrue();
        }

        [Test]
        public async Task ParseSoundEventBoolAcceptsOnlyOneAndTrue()
        {
            string[] accepted = ["true", "TRUE", "1", "1.0", "x1.0y"];
            string[] rejected = ["", "0", "2", "0.5", "yes", "-1"];

            foreach (var text in accepted)
            {
                await Assert.That(ValueParsing.ParseSoundEventBool(text)).IsTrue().Because($"\"{text}\"");
            }

            foreach (var text in rejected)
            {
                await Assert.That(ValueParsing.ParseSoundEventBool(text)).IsFalse().Because($"\"{text}\"");
            }
        }

        [Test]
        public async Task ParseVectorZeroFillsBadAndMissingTokens()
        {
            var three = new float[3];

            ValueParsing.ParseVector("  1   banana 3 4", three);
            await Assert.That(three).IsEquivalentTo([1f, 0f, 3f]);

            ValueParsing.ParseVector("5", three);
            await Assert.That(three).IsEquivalentTo([5f, 0f, 0f]);
        }

        [Test]
        public async Task ParseFloatArrayReadsPrefixesWithoutSeparators()
        {
            var destination = new float[4];

            await Assert.That(ValueParsing.ParseFloatArray("1-2", destination)).IsEqualTo(2);
            await Assert.That(destination).IsEquivalentTo([1f, -2f, 0f, 0f]);

            await Assert.That(ValueParsing.ParseFloatArray("1,2", destination)).IsEqualTo(1);
            await Assert.That(ValueParsing.ParseFloatArray("[1 2]", destination)).IsEqualTo(0);
        }

        [Test]
        public async Task TryParseColorWrapsComponentsToBytes()
        {
            await Assert.That(ValueParsing.TryParseColor("  1  2 3", out var rgb)).IsTrue();
            await Assert.That(rgb).IsEqualTo(new Color32(1, 2, 3, 255));

            await Assert.That(ValueParsing.TryParseColor("256 -1 x 7", out var rgba)).IsTrue();
            await Assert.That(rgba).IsEqualTo(new Color32(0, 255, 0, 7));

            await Assert.That(ValueParsing.TryParseColor("1 2", out _)).IsFalse();
            await Assert.That(ValueParsing.TryParseColor("1 2 3 4 5", out _)).IsFalse();
        }

        [Test]
        public async Task ParseLeadingReadsThePrefix()
        {
            await Assert.That(ValueParsing.ParseLeadingInt(" -3x")).IsEqualTo(-3);
            await Assert.That(ValueParsing.ParseLeadingInt("1.5")).IsEqualTo(1);
            await Assert.That(ValueParsing.ParseLeadingInt("x")).IsEqualTo(0);
            await Assert.That(ValueParsing.ParseLeadingFloat("2.5e1abc")).IsEqualTo(25f);
        }

        [Test]
        public async Task ParseLeadingFloatsSkipsEmptyTokens()
        {
            var values = new float[3];

            await Assert.That(ValueParsing.ParseLeadingFloats(",5", ",", values)).IsEqualTo(1);
            await Assert.That(values).IsEquivalentTo(new float[] { 5f, 0f, 0f });

            await Assert.That(ValueParsing.ParseLeadingFloats("5,,7", ",", values)).IsEqualTo(2);
            await Assert.That(values).IsEquivalentTo(new float[] { 5f, 7f, 0f });

            await Assert.That(ValueParsing.ParseLeadingFloats(",,", ",", values)).IsEqualTo(0);
            await Assert.That(values).IsEquivalentTo(new float[] { 0f, 0f, 0f });

            await Assert.That(ValueParsing.ParseLeadingFloats("1, 90 [2]", "[], ", values)).IsEqualTo(3);
            await Assert.That(values).IsEquivalentTo(new float[] { 1f, 90f, 2f });

            await Assert.That(ValueParsing.ParseLeadingFloats("5, 90", ",", values)).IsEqualTo(2);
            await Assert.That(values).IsEquivalentTo(new float[] { 5f, 90f, 0f });
        }
    }
}
