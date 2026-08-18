using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropEvaluationContextTest
    {
        private static readonly float[] OffsetVector = [1f, 2f, 3f];
        private static readonly float[] FirstComponentVector = [4f, 5f, 6f];
        private static readonly float[] TintVector = [0.25f, 0.5f, 0.75f, 1f];

        [Test]
        public async Task VariableLookupIsCaseInsensitiveAndOverridesWin()
        {
            var context = new SmartPropEvaluationContext(
                new Dictionary<string, object?> { ["Height"] = 10f },
                overrides: new Dictionary<string, object?> { ["HEIGHT"] = 99f });

            await Assert.That(context.GetVariable("height")).IsEqualTo(99f);
            await Assert.That(context.GetVariable("Height")).IsEqualTo(99f);

            context.SetOverride("width", 5f);
            await Assert.That(context.GetVariable("WIDTH")).IsEqualTo(5f);
        }

        [Test]
        public async Task GetVariableReturnsNullForUnknownNames()
        {
            var context = new SmartPropEvaluationContext();
            await Assert.That(context.GetVariable("missing")).IsNull();
        }

        [Test]
        public async Task WithInstanceUpdatesPlacementInfoAndSharesRng()
        {
            var context = new SmartPropEvaluationContext(seed: 42);
            var derived = context.WithInstance(instanceIndex: 3, instanceCount: 12, linearScale: 0.5f);

            await Assert.That(derived.InstanceIndex).IsEqualTo(3);
            await Assert.That(derived.InstanceCount).IsEqualTo(12);
            await Assert.That(derived.LinearScale).IsEqualTo(0.5f);

            // The derived context continues the parent's random sequence
            var reference = new SmartPropEvaluationContext(seed: 42);
            var first = SmartPropExpressionEvaluator.Evaluate("RandomFloat(0, 100)", reference);
            var second = SmartPropExpressionEvaluator.Evaluate("RandomFloat(0, 100)", reference);

            var parentDraw = SmartPropExpressionEvaluator.Evaluate("RandomFloat(0, 100)", context);
            var derivedDraw = SmartPropExpressionEvaluator.Evaluate("RandomFloat(0, 100)", derived);

            await Assert.That(parentDraw).IsEqualTo(first);
            await Assert.That(derivedDraw).IsEqualTo(second);
        }

        [Test]
        public async Task WithInstanceDoesNotLeakOverridesBackToParent()
        {
            var context = new SmartPropEvaluationContext(new Dictionary<string, object?> { ["x"] = 1f });
            var derived = context.WithInstance();
            derived.SetOverride("x", 2f);

            await Assert.That(context.GetVariable("x")).IsEqualTo(1f);
            await Assert.That(derived.GetVariable("x")).IsEqualTo(2f);
        }

        [Test]
        public async Task ResolveScalarHandlesLiteralsAndNumericStrings()
        {
            var context = new SmartPropEvaluationContext();

            await Assert.That(context.ResolveScalar(null, 8f)).IsEqualTo(8f);
            await Assert.That(context.ResolveScalar(new KVObject(3.5f))).IsEqualTo(3.5f);
            await Assert.That(context.ResolveScalar(new KVObject(2))).IsEqualTo(2f);
            await Assert.That(context.ResolveScalar(new KVObject(true))).IsEqualTo(1f);
            await Assert.That(context.ResolveScalar(new KVObject(false))).IsEqualTo(0f);
            await Assert.That(context.ResolveScalar(new KVObject("12.5"))).IsEqualTo(12.5f);
            await Assert.That(context.ResolveScalar(new KVObject(" 7 "))).IsEqualTo(7f);
            await Assert.That(context.ResolveScalar(new KVObject(""))).IsEqualTo(0f);
        }

        [Test]
        public async Task ResolveScalarHandlesBindings()
        {
            var context = new SmartPropEvaluationContext(new Dictionary<string, object?>
            {
                ["amount"] = 6f,
                ["vec"] = FirstComponentVector,
            });

            var expression = KVObject.Collection();
            expression["m_Expression"] = new KVObject("amount * 2");
            await Assert.That(context.ResolveScalar(expression)).IsEqualTo(12f);

            var source = KVObject.Collection();
            source["m_SourceName"] = new KVObject("amount");
            await Assert.That(context.ResolveScalar(source)).IsEqualTo(6f);

            // Vector variables resolve to their first component
            var vectorSource = KVObject.Collection();
            vectorSource["m_SourceName"] = new KVObject("VEC");
            await Assert.That(context.ResolveScalar(vectorSource)).IsEqualTo(4f);

            var components = KVObject.Collection();
            var componentList = KVObject.Array();
            componentList.Add(new KVObject(1.5f));
            componentList.Add(new KVObject(9f));
            components["m_Components"] = componentList;
            await Assert.That(context.ResolveScalar(components)).IsEqualTo(1.5f);

            var array = KVObject.Array();
            array.Add(new KVObject(11f));
            await Assert.That(context.ResolveScalar(array)).IsEqualTo(11f);

            var unknownSource = KVObject.Collection();
            unknownSource["m_SourceName"] = new KVObject("nope");
            await Assert.That(context.ResolveScalar(unknownSource, 3f)).IsEqualTo(3f);
        }

        [Test]
        public async Task ResolveScalarEvaluatesExpressionStrings()
        {
            var context = new SmartPropEvaluationContext();
            await Assert.That(context.ResolveScalar(new KVObject("1 + 1"))).IsEqualTo(2f);
            await Assert.That(context.ResolveScalar(new KVObject("not numeric"), 5f)).IsEqualTo(5f);
        }

    }
}
