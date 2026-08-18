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

        [Test]
        public async Task ResolveStringHandlesAllForms()
        {
            var context = new SmartPropEvaluationContext(new Dictionary<string, object?>
            {
                ["model"] = "models/props/example.vmdl",
                ["count"] = 3,
            });

            await Assert.That(context.ResolveString(new KVObject("literal.vmdl"))).IsEqualTo("literal.vmdl");
            await Assert.That(context.ResolveString(new KVObject(true))).IsEqualTo("true");
            await Assert.That(context.ResolveString(new KVObject(2.5f))).IsEqualTo("2.5");

            var source = KVObject.Collection();
            source["m_SourceName"] = new KVObject("MODEL");
            await Assert.That(context.ResolveString(source)).IsEqualTo("models/props/example.vmdl");

            // String fields keep the literal value in the expression text
            var expression = KVObject.Collection();
            expression["m_Expression"] = new KVObject("models/props/from_expression.vmdl");
            await Assert.That(context.ResolveString(expression)).IsEqualTo("models/props/from_expression.vmdl");

            await Assert.That(context.ResolveString(null, "fallback.vmdl")).IsEqualTo("fallback.vmdl");
        }

        [Test]
        public async Task ResolveVector3HandlesAllForms()
        {
            var context = new SmartPropEvaluationContext(new Dictionary<string, object?>
            {
                ["offset"] = OffsetVector,
            });

            var array = KVObject.Array();
            array.Add(new KVObject(1f));
            array.Add(new KVObject(2f));
            array.Add(new KVObject(3f));
            var fromArray = context.ResolveVector3(array);
            await Assert.That(fromArray.X).IsEqualTo(1f);
            await Assert.That(fromArray.Y).IsEqualTo(2f);
            await Assert.That(fromArray.Z).IsEqualTo(3f);

            var components = KVObject.Collection();
            var componentList = KVObject.Array();
            componentList.Add(new KVObject(4f));
            componentList.Add(new KVObject(5f));
            componentList.Add(new KVObject(6f));
            components["m_Components"] = componentList;
            var fromComponents = context.ResolveVector3(components);
            await Assert.That(fromComponents.X).IsEqualTo(4f);
            await Assert.That(fromComponents.Y).IsEqualTo(5f);
            await Assert.That(fromComponents.Z).IsEqualTo(6f);

            var source = KVObject.Collection();
            source["m_SourceName"] = new KVObject("OFFSET");
            var fromVariable = context.ResolveVector3(source);
            await Assert.That(fromVariable.X).IsEqualTo(1f);
            await Assert.That(fromVariable.Y).IsEqualTo(2f);
            await Assert.That(fromVariable.Z).IsEqualTo(3f);

            // Scalars and expressions broadcast to all axes
            var broadcast = context.ResolveVector3(new KVObject(5f));
            await Assert.That(broadcast).IsEqualTo(new Vector3(5f, 5f, 5f));

            var expression = KVObject.Collection();
            expression["m_Expression"] = new KVObject("2 * 3");
            var fromExpression = context.ResolveVector3(expression);
            await Assert.That(fromExpression).IsEqualTo(new Vector3(6f, 6f, 6f));

            // Short arrays fall back to the per-axis default
            var shortArray = KVObject.Array();
            shortArray.Add(new KVObject(8f));
            var padded = context.ResolveVector3(shortArray, new Vector3(0f, -1f, -2f));
            await Assert.That(padded).IsEqualTo(new Vector3(8f, -1f, -2f));
        }

        [Test]
        public async Task ResolveVector4HandlesColors()
        {
            var context = new SmartPropEvaluationContext(new Dictionary<string, object?>
            {
                ["tint"] = TintVector,
            });

            var array = KVObject.Array();
            array.Add(new KVObject(0.25f));
            array.Add(new KVObject(0.5f));
            array.Add(new KVObject(0.75f));
            array.Add(new KVObject(1f));
            var fromArray = context.ResolveVector4(array);
            await Assert.That(fromArray.X).IsEqualTo(0.25f);
            await Assert.That(fromArray.Y).IsEqualTo(0.5f);
            await Assert.That(fromArray.Z).IsEqualTo(0.75f);
            await Assert.That(fromArray.W).IsEqualTo(1f);

            var source = KVObject.Collection();
            source["m_SourceName"] = new KVObject("TINT");
            var fromVariable = context.ResolveVector4(source);
            await Assert.That(fromVariable.W).IsEqualTo(1f);
        }

        [Test]
        public async Task ResolveAnglesMatchesVectorResolution()
        {
            var context = new SmartPropEvaluationContext();
            var array = KVObject.Array();
            array.Add(new KVObject(10f));
            array.Add(new KVObject(20f));
            array.Add(new KVObject(30f));
            var angles = context.ResolveAngles(array);
            await Assert.That(angles).IsEqualTo(new Vector3(10f, 20f, 30f));
        }
    }

    public class SmartPropVariableMapTest
    {
        private static readonly float[] ExpectedOffset = [1f, 2f, 3f];
        private static readonly float[] ExpectedTint = [0.5f, 0.5f, 0.5f, 1f];
        private static readonly float[] ExpectedBound = [7f, 8f, 9f];
        private static readonly float[] ExpectedColor = [0.1f, 0.2f, 0.3f, 0.4f];

        [Test]
        public async Task BuildReturnsEmptyForMissingOrNullVariables()
        {
            await Assert.That(SmartPropVariableMap.Build(null)).IsEmpty();

            var emptyRoot = KVObject.Collection();
            await Assert.That(SmartPropVariableMap.Build(emptyRoot)).IsEmpty();

            var rootNoVariables = KVObject.Collection();
            rootNoVariables["m_Children"] = KVObject.Array();
            await Assert.That(SmartPropVariableMap.Build(rootNoVariables)).IsEmpty();
        }

        [Test]
        public async Task BuildCoercesTypedDefaults()
        {
            var root = KVObject.Collection();
            var variables = KVObject.Array();
            variables.Add(Variable("Float", "Height", new KVObject(12.5f)));
            variables.Add(Variable("Int", "Count", new KVObject("3")));
            variables.Add(Variable("Bool", "Enabled", new KVObject("true")));
            variables.Add(Variable("Bool", "Other", new KVObject(1)));
            variables.Add(Variable("String", "Model", new KVObject("models/x.vmdl")));
            variables.Add(Variable("Vector3D", "Offset", Vec3(1f, 2f, 3f)));
            variables.Add(Variable("Color", "Tint", Vec4(0.5f, 0.5f, 0.5f, 1f)));
            variables.Add(Variable("Float", "NoDefault", null));
            root["m_Variables"] = variables;

            var map = SmartPropVariableMap.Build(root);

            await Assert.That(map["Height"]).IsEqualTo(12.5f);
            await Assert.That(map["Count"]).IsEqualTo(3);
            await Assert.That((bool)map["Enabled"]!).IsTrue();
            await Assert.That((bool)map["Other"]!).IsTrue();
            await Assert.That(map["Model"]).IsEqualTo("models/x.vmdl");
            await Assert.That((float[])map["Offset"]!).IsEquivalentTo(ExpectedOffset, CollectionOrdering.Matching);
            await Assert.That((float[])map["Tint"]!).IsEquivalentTo(ExpectedTint, CollectionOrdering.Matching);
            await Assert.That(map["NoDefault"]).IsEqualTo(0f);
        }

        [Test]
        public async Task VariableLookupThroughContextIsCaseInsensitive()
        {
            var root = KVObject.Collection();
            var variables = KVObject.Array();
            variables.Add(Variable("Float", "Width", new KVObject(4f)));
            root["m_Variables"] = variables;

            var context = new SmartPropEvaluationContext(SmartPropVariableMap.Build(root));
            await Assert.That(context.GetVariable("width")).IsEqualTo(4f);
            await Assert.That(SmartPropExpressionEvaluator.Evaluate("Width * 2", context)).IsEqualTo(8f);
        }

        [Test]
        public async Task BuildResolvesVariableReferenceBindings()
        {
            var root = KVObject.Collection();
            var variables = KVObject.Array();
            variables.Add(Variable("Float", "Base", new KVObject(5f)));
            variables.Add(Variable("Float", "Derived", Binding("m_SourceName", "Base")));
            variables.Add(Variable("Float", "Chained", Binding("m_SourceName", "Derived")));
            root["m_Variables"] = variables;

            var map = SmartPropVariableMap.Build(root);
            await Assert.That(map["Derived"]).IsEqualTo(5f);
            await Assert.That(map["Chained"]).IsEqualTo(5f);
        }

        [Test]
        public async Task BuildResolvesExpressionBindings()
        {
            var root = KVObject.Collection();
            var variables = KVObject.Array();
            variables.Add(Variable("Float", "Base", new KVObject(4f)));
            variables.Add(Variable("Float", "Computed", Binding("m_Expression", new KVObject("Base * 3 + 1"))));
            root["m_Variables"] = variables;

            var map = SmartPropVariableMap.Build(root);
            await Assert.That(map["Computed"]).IsEqualTo(13f);
        }

        [Test]
        public async Task BuildResolvesVectorBindingsWithPerTypeArity()
        {
            var root = KVObject.Collection();
            var variables = KVObject.Array();
            variables.Add(Variable("Vector3D", "Source", Vec3(7f, 8f, 9f)));
            variables.Add(Variable("Vector3D", "Bound", Binding("m_SourceName", "Source")));
            variables.Add(Variable("Color", "ColorBound", Binding("m_SourceName", "SomeColor")));
            variables.Add(Variable("Color", "SomeColor", Vec4(0.1f, 0.2f, 0.3f, 0.4f)));
            root["m_Variables"] = variables;

            var map = SmartPropVariableMap.Build(root);
            await Assert.That((float[])map["Bound"]!).IsEquivalentTo(ExpectedBound, CollectionOrdering.Matching);
            await Assert.That((float[])map["ColorBound"]!).IsEquivalentTo(ExpectedColor, CollectionOrdering.Matching);
        }

        [Test]
        public async Task CircularBindingsTerminateOnZeroValues()
        {
            var root = KVObject.Collection();
            var variables = KVObject.Array();
            variables.Add(Variable("Float", "A", Binding("m_SourceName", "B")));
            variables.Add(Variable("Float", "B", Binding("m_SourceName", "A")));
            root["m_Variables"] = variables;

            var map = SmartPropVariableMap.Build(root);
            await Assert.That(map["A"]).IsEqualTo(0f);
            await Assert.That(map["B"]).IsEqualTo(0f);
        }

        [Test]
        public async Task EntriesWithoutNamesAreSkipped()
        {
            var root = KVObject.Collection();
            var variables = KVObject.Array();
            variables.Add(Variable("Float", null, new KVObject(1f)));
            var entry = KVObject.Collection();
            entry["generic_data_type"] = new KVObject("CSmartPropVariable_Float");
            variables.Add(entry);
            root["m_Variables"] = variables;

            await Assert.That(SmartPropVariableMap.Build(root)).IsEmpty();
        }

        private static KVObject Variable(string className, string? name, KVObject? defaultValue)
        {
            var entry = KVObject.Collection();
            entry["generic_data_type"] = new KVObject($"CSmartPropVariable_{className}");
            if (name != null)
            {
                entry["m_VariableName"] = new KVObject(name);
            }

            if (defaultValue != null)
            {
                entry["m_DefaultValue"] = defaultValue;
            }

            return entry;
        }

        private static KVObject Binding(string key, KVObject value)
        {
            var binding = KVObject.Collection();
            binding[key] = value;
            return binding;
        }

        private static KVObject Vec3(float x, float y, float z)
        {
            var array = KVObject.Array();
            array.Add(new KVObject(x));
            array.Add(new KVObject(y));
            array.Add(new KVObject(z));
            return array;
        }

        private static KVObject Vec4(float x, float y, float z, float w)
        {
            var array = KVObject.Array();
            array.Add(new KVObject(x));
            array.Add(new KVObject(y));
            array.Add(new KVObject(z));
            array.Add(new KVObject(w));
            return array;
        }
    }
}
