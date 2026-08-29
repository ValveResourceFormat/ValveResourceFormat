using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.ResourceTypes.ModelFlex.FlexOps;

namespace Tests
{
    public class FlexRuleTest
    {
        private static FlexOp Op(string opCode, int data = 0) => FlexOp.Build(opCode, data)!;

        // FLEX_OP_CONST carries its float payload as raw bits in the int data
        private static FlexOp Const(float value) => Op("FLEX_OP_CONST", BitConverter.SingleToInt32Bits(value));

        [Test]
        [Arguments("FLEX_OP_ADD", 10f, 4f, 14f)]
        [Arguments("FLEX_OP_SUB", 10f, 4f, 6f)]
        [Arguments("FLEX_OP_MUL", 10f, 4f, 40f)]
        [Arguments("FLEX_OP_DIV", 10f, 4f, 2.5f)]
        [Arguments("FLEX_OP_MIN", 10f, 4f, 4f)]
        [Arguments("FLEX_OP_MAX", 10f, 4f, 10f)]
        public async Task EvaluatesBinaryOperations(string opCode, float first, float second, float expected)
        {
            var rule = new FlexRule(0, [Const(first), Const(second), Op(opCode)]);

            await Assert.That(rule.Evaluate([])).IsEqualTo(expected).Within(0.0001f);
        }

        [Test]
        public async Task FetchReadsControllerValues()
        {
            var rule = new FlexRule(0, [Op("FLEX_OP_FETCH1", 1), Op("FLEX_OP_FETCH1", data: 0), Op("FLEX_OP_SUB")]);

            await Assert.That(rule.Evaluate([2f, 7f])).IsEqualTo(5f).Within(0.0001f);
        }

        [Test]
        [Arguments(-1f, 0f)]
        [Arguments(0.5f, 0.4f)] // ramping in between t1 and t2
        [Arguments(1.5f, 0.8f)] // plateau between t2 and t3
        [Arguments(2.5f, 0.4f)] // ramping out between t3 and t4
        [Arguments(4f, 0f)]
        public async Task EvaluatesNWayBlend(float tCurrent, float expected)
        {
            // Controller 0 drives the blend position, controller 1 holds the blended value.
            // The t controller index rides the stack as raw int bits pushed by a const.
            var rule = new FlexRule(0,
            [
                Const(0f), // t1
                Const(1f), // t2
                Const(2f), // t3
                Const(3f), // t4
                Op("FLEX_OP_CONST", data: 0),
                Op("FLEX_OP_NWAY", 1),
            ]);

            await Assert.That(rule.Evaluate([tCurrent, 0.8f])).IsEqualTo(expected).Within(0.0001f);
        }

        [Test]
        public async Task RejectsInvalidPrograms()
        {
            await Assert.That(FlexOp.Build("FLEX_OP_DOES_NOT_EXIST", 0)).IsNull();

            Assert.Throws<ArgumentException>(() => _ = new FlexRule(0, []));

            // A program that leaves more than one value on the stack is malformed.
            var unbalanced = new FlexRule(0, [Const(1f), Const(2f)]);
            Assert.Throws<InvalidOperationException>(() => unbalanced.Evaluate([]));
        }
    }
}
