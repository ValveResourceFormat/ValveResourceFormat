using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Utils;

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
        [Arguments("(v).y", 2f)]
        [Arguments("((v)).z", 3f)]
        [Arguments("TRUE", 1f)]
        [Arguments("False", 0f)]
        [Arguments("010", 10f)]
        [Arguments("0x10", 16f)]
        [Arguments("0xFFFFFFFF", -1f)]
        [Arguments("1'000", 1f)]
        [Arguments(".5", 0.5f)]
        [Arguments("1.", 1f)]
        [Arguments("1e3", 1000f)]
        [Arguments("2E-1", 0.2f)]
        [Arguments("round(2.5)", 3f)]
        [Arguments("round(-2.5)", -3f)]
        [Arguments("clamp(5, 0, 3)", 3f)]
        [Arguments("clamp(0 / 0, 2, 5)", 2f)]
        [Arguments("saturate(0 / 0)", 0f)]
        [Arguments("smoothstep(1, 1, 1)", 0f)]
        [Arguments("lerp(0, 10, 0.25)", 2.5f)]
        [Arguments("smoothstep(0, 1, 0.5)", 0.5f)]
        [Arguments("sign(-3)", -1f)]
        [Arguments("min(1, 2) + max(1, 2)", 3f)]
        [Arguments("Saturate(4)", 1f)]
        [Arguments("/* comment */ 3", 3f)]
        [Arguments("3 // comment\n", 3f)]
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
        [Arguments("(v)")]
        [Arguments("v + 1")]
        [Arguments("v.w")]
        [Arguments("x.x")]
        [Arguments("x.y")]
        [Arguments("v.xy")]
        [Arguments("(v + 1).x")]
        [Arguments("09")]
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

        [Test]
        [Arguments("InstanceIndex()", 0f)]
        [Arguments("LinearScale()", 1f)]
        [Arguments("RandomFloat(0, 1)", 0f)]
        public async Task ExpressionOutsideEvaluationUsesDefaults(string expression, float expected)
        {
            await Assert.That(SmartPropExpression.Evaluate(expression)).IsEqualTo(expected);
        }

        [Test]
        public async Task BoolConversionTruncatesNumbersAndParsesWords()
        {
            await Assert.That(SmartPropValue.FromFloat(0.5).GetBool()).IsFalse();
            await Assert.That(SmartPropValue.FromFloat(-1.5).GetBool()).IsTrue();
            await Assert.That(SmartPropValue.FromString("yes").GetBool()).IsTrue();
            await Assert.That(SmartPropValue.FromString("No").GetBool()).IsFalse();
            await Assert.That(SmartPropValue.FromString("TRUE").GetBool()).IsTrue();
            await Assert.That(SmartPropValue.FromString("2").GetBool()).IsTrue();
            await Assert.That(SmartPropValue.FromString("2.5").GetBool()).IsFalse();
            await Assert.That(SmartPropValue.FromString(string.Empty).GetBool()).IsFalse();
        }

        [Test]
        public async Task StringConversionOnlyReadsStrings()
        {
            await Assert.That(SmartPropValue.FromFloat(2.5).GetString()).IsEqualTo(string.Empty);
            await Assert.That(SmartPropValue.FromInt(3).GetString()).IsEqualTo(string.Empty);
            await Assert.That(SmartPropValue.FromBool(true).GetString()).IsEqualTo(string.Empty);
        }

        [Test]
        [Arguments(" 42", 42)]
        [Arguments("-7", -7)]
        [Arguments("12abc", 0)]
        [Arguments("2.5", 0)]
        [Arguments("42 ", 0)]
        [Arguments("99999999999", 0)]
        public async Task IntConversionParsesWholeStrings(string text, int expected)
        {
            await Assert.That(SmartPropValue.FromString(text).GetInt()).IsEqualTo(expected);
        }

        [Test]
        [Arguments(" 2.5", 2.5f)]
        [Arguments("+1.5", 1.5f)]
        [Arguments("1e3", 1000f)]
        [Arguments(".5", 0.5f)]
        [Arguments("3px", 0f)]
        [Arguments("2.5f", 0f)]
        [Arguments("2.5 ", 0f)]
        [Arguments("1e", 0f)]
        public async Task FloatConversionParsesWholeStrings(string text, float expected)
        {
            await Assert.That(SmartPropValue.FromString(text).GetFloat()).IsEqualTo(expected);
        }

        [Test]
        public async Task VectorConversionNeedsTheExactCount()
        {
            await Assert.That(SmartPropValue.FromString("1 2 3").GetVector(3)).IsEqualTo(new Vector4(1f, 2f, 3f, 0f));
            await Assert.That(SmartPropValue.FromString("1 2").GetVector(3)).IsEqualTo(Vector4.Zero);
            await Assert.That(SmartPropValue.FromString(" 1  2   3 ").GetVector(3)).IsEqualTo(new Vector4(1f, 2f, 3f, 0f));
            await Assert.That(SmartPropValue.FromString("1	2 3").GetVector(3)).IsEqualTo(Vector4.Zero);
            await Assert.That(SmartPropValue.FromArray(1, 2).GetVector(3)).IsEqualTo(Vector4.Zero);
            await Assert.That(SmartPropValue.FromArray(1, 2, 3, 4).GetVector(3)).IsEqualTo(Vector4.Zero);
            await Assert.That(SmartPropValue.FromFloat(5).GetVector(3)).IsEqualTo(Vector4.Zero);
        }

        [Test]
        public async Task ColorConversionNeedsThreeOrFourChannels()
        {
            await Assert.That(SmartPropValue.FromString("255 0 0").GetColor()).IsEqualTo(new Color32(255, 0, 0, 255));
            await Assert.That(SmartPropValue.FromArray(1, 2, 3, 4).GetColor()).IsEqualTo(new Color32(1, 2, 3, 4));
            await Assert.That(SmartPropValue.FromArray(1, 2).GetColor()).IsEqualTo(new Color32(0, 0, 0, 255));
            await Assert.That(SmartPropValue.FromInt(7).GetColor()).IsEqualTo(new Color32(0, 0, 0, 255));
        }

        [Test]
        public async Task StringsOrderByAsciiLowerCase()
        {
            await Assert.That(SmartPropValue.FromString("a_").IsGreater(SmartPropValue.FromString("aB"))).IsFalse();
            await Assert.That(SmartPropValue.FromString("aB").IsGreater(SmartPropValue.FromString("a_"))).IsTrue();
            await Assert.That(SmartPropValue.FromString("ab").IsGreater(SmartPropValue.FromString("AB"))).IsFalse();
        }

        [Test]
        public async Task IntegerAndFloatArraysAreNotEqual()
        {
            var integers = SmartPropValue.FromColor(new Color32(1, 2, 3, 255));

            await Assert.That(integers.IsEqual(SmartPropValue.FromArray(1, 2, 3))).IsFalse();
            await Assert.That(integers.IsEqual(SmartPropValue.FromColor(new Color32(1, 2, 3, 255)))).IsTrue();
        }
    }
}
