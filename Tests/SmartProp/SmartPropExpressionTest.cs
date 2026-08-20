using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropExpressionTest
    {
        private static readonly float[] MemberVector = [1f, 2f, 3f];
        private static readonly float[] MemberColor = [0.1f, 0.2f, 0.3f, 0.4f];
        private static readonly string[] MalformedExpressions =
        [
            "", "  ", "((", "1 +", "foo(", "1 2", "a..b", "v.q", "1 @ 2", ")", "?:", "1 ?",
        ];
        [Test]
        public async Task EvaluatesNumberLiterals()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("42")).IsEqualTo(42f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("3.5")).IsEqualTo(3.5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate(".5")).IsEqualTo(0.5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("-7")).IsEqualTo(-7f);
        }

        [Test]
        public async Task EvaluatesConstants()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("true")).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("false")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("pi")).IsEqualTo(MathF.PI);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("e")).IsEqualTo(MathF.E);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("PI")).IsEqualTo(MathF.PI);
        }

        [Test]
        public async Task RespectsOperatorPrecedence()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("1+2*3")).IsEqualTo(7f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("(1+2)*3")).IsEqualTo(9f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("10-4-3")).IsEqualTo(3f);
        }

        [Test]
        public async Task EvaluatesUnaryOperators()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("-5")).IsEqualTo(-5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("!0")).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("!1")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("!!7")).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("+-3")).IsEqualTo(-3f);
        }

        [Test]
        public async Task DivisionAndModuloByZeroReturnZero()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("10/4")).IsEqualTo(2.5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("1/0")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("5 % 3")).IsEqualTo(2f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("5 % 0")).IsEqualTo(0f);
            // C-style modulo keeps the sign of the dividend
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("-7 % 3")).IsEqualTo(-1f);
        }

        [Test]
        public async Task EvaluatesComparisons()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("2 == 2")).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("2 != 2")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("1 < 2")).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("2 <= 1")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("3 > 4")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("4 >= 4")).IsEqualTo(1f);
        }

        [Test]
        public async Task EvaluatesLogicOperators()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("1 && 0")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("1 && 2")).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("0 || 0")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("0 || 3")).IsEqualTo(1f);
        }

        [Test]
        public async Task EvaluatesTernaryConditionals()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("(1 == 1) ? 5 : 6")).IsEqualTo(5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("(0 == 1) ? 5 : 6")).IsEqualTo(6f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("1 > 0 ? 1 : 1 > 2 ? 2 : 3")).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("0 > 0 ? 1 : 1 > 2 ? 2 : 3")).IsEqualTo(3f);
        }

        [Test]
        public async Task EvaluatesMathFunctions()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("abs(-3)")).IsEqualTo(3f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("min(1, 2, 0.5)")).IsEqualTo(0.5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("max(1, 9, 4)")).IsEqualTo(9f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("clamp(5, 1, 3)")).IsEqualTo(3f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("clamp(0, 1, 3)")).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("lerp(0, 10, 0.25)")).IsEqualTo(2.5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("sign(-9)")).IsEqualTo(-1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("sqrt(9)")).IsEqualTo(3f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("sqrt(-1)")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("pow(2, 8)")).IsEqualTo(256f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("floor(1.7)")).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("ceil(1.2)")).IsEqualTo(2f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("round(2.5)")).IsEqualTo(MathF.Round(2.5f));
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("atan2(1, 1)")).IsEqualTo(MathF.Atan2(1f, 1f));
        }

        [Test]
        public async Task FunctionNamesAreCaseInsensitive()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("SIN(0)")).IsEqualTo(0f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("Deg2Rad(180)")).IsEqualTo(180f * (MathF.PI / 180f));
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("Rad2Deg(pi)")).IsEqualTo(MathF.PI * (180f / MathF.PI));
        }

        [Test]
        public async Task EvaluatesInstanceFunctions()
        {
            var context = new SmartPropEvaluationContext(instanceIndex: 5, instanceCount: 7);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("InstanceIndex()", context)).IsEqualTo(5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("InstanceCount()", context)).IsEqualTo(7f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("INSTANCEINDEX()", context)).IsEqualTo(5f);
        }

        [Test]
        public async Task EvaluatesLinearScaleForms()
        {
            var context = new SmartPropEvaluationContext(linearScale: 2.5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("LinearScale()", context)).IsEqualTo(2.5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("LinearScale(5, 0, 10)", context)).IsEqualTo(0.5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("LinearScale(5, 0, 10, 0, 100)", context)).IsEqualTo(50f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("LinearScale(4)", context)).IsEqualTo(4f);
        }

        [Test]
        public async Task RandomFunctionsAreDeterministicPerSeed()
        {
            var contextA = new SmartPropEvaluationContext(seed: 1234);
            var contextB = new SmartPropEvaluationContext(seed: 1234);

            for (var i = 0; i < 8; i++)
            {
                var a = SmartPropExpressionEvaluator.Evaluate("RandomFloat(2, 4)", contextA);
                var b = SmartPropExpressionEvaluator.Evaluate("RandomFloat(2, 4)", contextB);
                await Assert.That(a).IsEqualTo(b);
                await Assert.That(a >= 2f && a <= 4f).IsTrue();
            }

            for (var i = 0; i < 8; i++)
            {
                var a = SmartPropExpressionEvaluator.Evaluate("RandomInt(1, 6)", contextA);
                var b = SmartPropExpressionEvaluator.Evaluate("RandomInt(1, 6)", contextB);
                await Assert.That(a).IsEqualTo(b);
                await Assert.That(a >= 1f && a <= 6f).IsTrue();
            }
        }

        [Test]
        public async Task ResolvesVariablesFromContext()
        {
            var context = new SmartPropEvaluationContext(new Dictionary<string, object?>
            {
                ["scale"] = 3f,
                ["count"] = 4,
                ["flag"] = true,
            });

            await Assert.That(SmartPropExpressionEvaluator.Evaluate("scale * 2", context)).IsEqualTo(6f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("count + 1", context)).IsEqualTo(5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("flag", context)).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("unknown_name", context)).IsEqualTo(0f);
        }

        [Test]
        public async Task ResolvesVectorMembers()
        {
            var context = new SmartPropEvaluationContext(new Dictionary<string, object?>
            {
                ["v"] = MemberVector,
                ["color"] = MemberColor,
                ["uniform"] = 7f,
            });

            await Assert.That(SmartPropExpressionEvaluator.Evaluate("v.x", context)).IsEqualTo(1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("v.y", context)).IsEqualTo(2f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("v.z", context)).IsEqualTo(3f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("color.r", context)).IsEqualTo(0.1f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("color.a", context)).IsEqualTo(0.4f);
            // A scalar variable broadcasts to any member index
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("uniform.y", context)).IsEqualTo(7f);
        }

        [Test]
        public async Task StripsLineComments()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("1 + 2 // trailing comment")).IsEqualTo(3f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("// leading comment\n5")).IsEqualTo(5f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("3 // a\n + 4 // b")).IsEqualTo(7f);
        }

        [Test]
        public async Task MalformedExpressionsReturnDefaultInsteadOfThrowing()
        {
            foreach (var bad in MalformedExpressions)
            {
                await Assert.That(SmartPropExpressionEvaluator.Evaluate(bad, null, 42f)).IsEqualTo(42f);
            }

            await Assert.That(SmartPropExpressionEvaluator.Evaluate(null, null, 7f)).IsEqualTo(7f);
        }

        [Test]
        public async Task UnknownFunctionReturnsDefault()
        {
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("NotAFunction(1)", null, 9f)).IsEqualTo(9f);
        }
    }
}
