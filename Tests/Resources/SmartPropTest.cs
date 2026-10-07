using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.Resources
{
    public class SmartPropTest
    {
        private static readonly Dictionary<string, float[]> Variables = new()
        {
            ["x"] = [2f],
            ["near"] = [1.0005f],
            ["one"] = [1f],
            ["zero"] = [0f],
            ["v"] = [1f, 2f, 3f],
        };

        [Test]
        public async Task RandomStreamMatchesSourceSamples()
        {
            var stream = new UniformRandomStream(1);
            float[] floats = [stream.RandomFloat(0f, 1f), stream.RandomFloat(0f, 1f), stream.RandomFloat(0f, 1f), stream.RandomFloat(0f, 1f), stream.RandomFloat(0f, 1f)];

            stream.SetSeed(12345);
            int[] ints = [stream.RandomInt(0, 0x7FFFFFFE), stream.RandomInt(0, 0x7FFFFFFE), stream.RandomInt(0, 0x7FFFFFFE), stream.RandomInt(0, 0x7FFFFFFE), stream.RandomInt(0, 0x7FFFFFFE)];

            await Assert.That(floats).IsEquivalentTo([0.4159993529319763f, 0.09196489304304123f, 0.7564104795455933f, 0.5297002196311951f, 0.9304364919662476f]);
            await Assert.That(ints).IsEquivalentTo([1982386332, 715426902, 424962143, 2038867620, 1683198519]);
        }

        [Test]
        public async Task RandomStreamSeedsZeroOneAndNegativeAreEqual()
        {
            foreach (var seed in (int[])[0, 1, -1, int.MinValue])
            {
                var stream = new UniformRandomStream(seed);
                int[] values = [stream.RandomInt(0, 0x7FFFFFFE), stream.RandomInt(0, 0x7FFFFFFE), stream.RandomInt(0, 0x7FFFFFFE)];
                await Assert.That(values).IsEquivalentTo([893351816, 197493099, 1624379149]);
            }
        }

        [Test]
        public async Task RandomStreamDoesNotDrawForSingleValueRange()
        {
            var stream = new UniformRandomStream(7);
            var reference = new UniformRandomStream(7);

            await Assert.That(stream.RandomInt(5, 5)).IsEqualTo(5);
            await Assert.That(stream.RandomInt(5, 4)).IsEqualTo(5);
            await Assert.That(stream.RandomFloat()).IsEqualTo(reference.RandomFloat());
        }

        [Test]
        [Arguments("1 + 2 * 3", 7f)]
        [Arguments("-2 * 3", -6f)]
        [Arguments("10 % 4", 2f)]
        [Arguments("7 / 2", 3.5f)]
        [Arguments("-(-1)", 1f)]
        [Arguments("!0", 1f)]
        [Arguments("1 < 2", 1f)]
        [Arguments("x > 1 ? 10 : 20", 10f)]
        [Arguments("(x > 1) ? 10 : 20", 10f)]
        [Arguments("x>1?10:20", 10f)]
        [Arguments("v.y", 2f)]
        [Arguments("v.G", 2f)]
        [Arguments("v.z + v.x", 4f)]
        [Arguments("TRUE", 1f)]
        [Arguments("False", 0f)]
        [Arguments("010", 10f)]
        [Arguments("0x10", 16f)]
        [Arguments("1'000", 1f)]
        [Arguments(".5", 0.5f)]
        [Arguments("1.", 1f)]
        [Arguments("1e3", 1000f)]
        [Arguments("2E-1", 0.2f)]
        [Arguments("round(2.5)", 3f)]
        [Arguments("round(-2.5)", -3f)]
        [Arguments("clamp(5, 0, 3)", 3f)]
        [Arguments("lerp(0, 10, 0.25)", 2.5f)]
        [Arguments("smoothstep(0, 1, 0.5)", 0.5f)]
        [Arguments("sign(-3)", -1f)]
        [Arguments("min(1, 2) + max(1, 2)", 3f)]
        [Arguments("Saturate(4)", 1f)]
        [Arguments("/* comment */ 3", 3f)]
        [Arguments("3 // comment\n", 3f)]
        [Arguments("InstanceIndex()", 0f)]
        [Arguments("LinearScale()", 1f)]
        [Arguments("RandomFloat(0, 1)", 0f)]
        public async Task ExpressionEvaluates(string expression, float expected)
        {
            await Assert.That(SmartPropExpression.Evaluate(expression, Variables)).IsEqualTo(expected).Within(1e-5f);
        }

        [Test]
        [Arguments("near == 1", 1f)]
        [Arguments("near != 1", 0f)]
        [Arguments("1.0005 == 1", 0f)]
        [Arguments("1.0005 != 1", 1f)]
        [Arguments("x && 3", 3f)]
        [Arguments("x || 5", 2f)]
        [Arguments("zero || x", 2f)]
        [Arguments("zero && x", 0f)]
        [Arguments("2 && 3", 1f)]
        [Arguments("2 || 3", 1f)]
        public async Task ExpressionComparesWithToleranceAndShortCircuitsUnlessFolded(string expression, float expected)
        {
            await Assert.That(SmartPropExpression.Evaluate(expression, Variables)).IsEqualTo(expected);
        }

        [Test]
        [Arguments("one?x:zero")]
        [Arguments("1 ? 2 : 0 ? 3 : 4")]
        [Arguments("1 < 2 < 3")]
        [Arguments("1 == 1 == 1")]
        [Arguments("--1")]
        [Arguments("!!1")]
        [Arguments("x // trailing comment")]
        [Arguments("'text'")]
        [Arguments("\"text\"")]
        [Arguments("$x")]
        [Arguments("@x")]
        [Arguments("unknown")]
        [Arguments("unknownfunction(1)")]
        [Arguments("min(1)")]
        [Arguments("InstanceCount")]
        [Arguments("v")]
        [Arguments("v.w")]
        [Arguments("x.y")]
        [Arguments("v.xy")]
        [Arguments("(v).x")]
        [Arguments("1 +")]
        [Arguments("x = 1")]
        [Arguments("")]
        public async Task ExpressionFailsToCompile(string expression)
        {
            await Assert.That(SmartPropExpression.Compile(expression, Variables)).IsNull();
            await Assert.That(SmartPropExpression.Evaluate(expression, Variables)).IsEqualTo(0f);
        }

        [Test]
        public async Task ExpressionTernaryWithSpacesCompiles()
        {
            await Assert.That(SmartPropExpression.Evaluate("one ? x : zero", Variables)).IsEqualTo(2f);
        }
    }
}
