using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropEvaluatorTest
    {
        private const float Tolerance = 1e-3f;

        private static KVObject Root(params KVObject[] children)
        {
            var root = KVObject.Collection();
            root["generic_data_type"] = new KVObject("CSmartPropRoot");
            var list = KVObject.Array();
            foreach (var child in children)
            {
                list.Add(child);
            }

            root["m_Children"] = list;
            root["m_Variables"] = KVObject.Array();
            return root;
        }

        private static KVObject Element(string className, int elementId, params KVObject[] children)
        {
            var element = KVObject.Collection();
            element["generic_data_type"] = new KVObject($"CSmartPropElement_{className}");
            element["m_nElementID"] = new KVObject(elementId);
            element["m_Modifiers"] = KVObject.Array();
            var list = KVObject.Array();
            foreach (var child in children)
            {
                list.Add(child);
            }

            element["m_Children"] = list;
            return element;
        }

        private static KVObject ModelElement(int elementId, string modelName, params KVObject[] modifiers)
        {
            var element = Element("Model", elementId);
            element["m_sModelName"] = new KVObject(modelName);
            return WithModifiers(element, modifiers);
        }

        private static KVObject WithModifiers(KVObject element, params KVObject[] modifiers)
        {
            var list = KVObject.Array();
            foreach (var modifier in modifiers)
            {
                list.Add(modifier);
            }

            element["m_Modifiers"] = list;
            return element;
        }

        private static KVObject Modifier(string className, params (string Key, KVObject Value)[] fields)
        {
            var modifier = KVObject.Collection();
            modifier["generic_data_type"] = new KVObject($"CSmartPropOperation_{className}");
            foreach (var (key, value) in fields)
            {
                modifier[key] = value;
            }

            return modifier;
        }

        private static KVObject Vec(float x, float y, float z)
        {
            var array = KVObject.Array();
            array.Add(new KVObject(x));
            array.Add(new KVObject(y));
            array.Add(new KVObject(z));
            return array;
        }

        private static KVObject PathPoint(float x, float y, float z) => Vec(x, y, z);

        private static KVObject ArrayOf(params KVObject[] items)
        {
            var array = KVObject.Array();
            foreach (var item in items)
            {
                array.Add(item);
            }

            return array;
        }

        private static KVObject Criteria(string className, params (string Key, KVObject Value)[] fields)
        {
            var node = KVObject.Collection();
            node["generic_data_type"] = new KVObject($"CSmartPropSelectionCriteria_{className}");
            foreach (var (key, value) in fields)
            {
                node[key] = value;
            }

            return node;
        }

        [Test]
        public async Task ModelElementsProduceEvaluatedModels()
        {
            var root = Root(ModelElement(1, "models/a.vmdl"));
            var result = SmartPropEvaluator.Evaluate(root);

            await Assert.That(result.Models).HasSingleItem();
            var model = result.Models[0];
            await Assert.That(model.ElementId).IsEqualTo(1);
            await Assert.That(model.ModelName).IsEqualTo("models/a.vmdl");
            await Assert.That(model.WorldMatrix).IsEqualTo(Matrix4x4.Identity);
            await Assert.That(model.Position).IsEqualTo(Vector3.Zero);
        }

        [Test]
        public async Task RootAndParentModifiersCascadeToChildren()
        {
            var root = WithModifiers(Root(ModelElement(1, "models/a.vmdl")),
                Modifier("Translate", ("m_vPosition", Vec(100f, 0f, 0f))));
            var result = SmartPropEvaluator.Evaluate(root);

            await Assert.That(result.Models[0].Position.X).IsEqualTo(100f).Within(Tolerance);

            var group = WithModifiers(Element("Group", 2, ModelElement(1, "models/a.vmdl")),
                Modifier("Translate", ("m_vPosition", Vec(0f, 50f, 0f))));
            var nested = SmartPropEvaluator.Evaluate(Root(group));
            await Assert.That(nested.Models[0].Position.Y).IsEqualTo(50f).Within(Tolerance);
        }

        [Test]
        public async Task DisabledElementsAreSkipped()
        {
            var disabled = ModelElement(1, "models/a.vmdl");
            disabled["m_bEnabled"] = new KVObject(false);
            var enabled = ModelElement(2, "models/b.vmdl");

            var result = SmartPropEvaluator.Evaluate(Root(disabled, enabled));
            await Assert.That(result.Models).HasSingleItem();
            await Assert.That(result.Models[0].ElementId).IsEqualTo(2);
        }

        [Test]
        public async Task ElementsWithoutIdsProduceNoModelEntries()
        {
            var noId = KVObject.Collection();
            noId["generic_data_type"] = new KVObject("CSmartPropElement_Model");
            noId["m_sModelName"] = new KVObject("models/x.vmdl");
            noId["m_Modifiers"] = KVObject.Array();

            var result = SmartPropEvaluator.Evaluate(Root(noId));
            await Assert.That(result.Models).IsEmpty();
        }

        [Test]
        public async Task ModelScaleShowsInDecomposedTransform()
        {
            var model = ModelElement(1, "models/a.vmdl");
            model["m_flUniformModelScale"] = new KVObject(3f);

            var result = SmartPropEvaluator.Evaluate(Root(model));
            await Assert.That(result.Models[0].Scale.X).IsEqualTo(3f).Within(Tolerance);
        }

        [Test]
        public async Task PickOneSpecificSelectsTheChosenChild()
        {
            var pickOne = Element("PickOne", 10,
                ModelElement(1, "models/a.vmdl"),
                ModelElement(2, "models/b.vmdl"),
                ModelElement(3, "models/c.vmdl"));
            pickOne["m_SelectionMode"] = new KVObject("SPECIFIC");
            pickOne["m_SpecificChildIndex"] = new KVObject(2);

            var result = SmartPropEvaluator.Evaluate(Root(pickOne));
            await Assert.That(result.Models).HasSingleItem();
            await Assert.That(result.Models[0].ModelName).IsEqualTo("models/c.vmdl");
        }

        [Test]
        public async Task PickOneRandomTakesFirstChildAndClampsSpecificIndex()
        {
            var random = Element("PickOne", 10,
                ModelElement(1, "models/a.vmdl"),
                ModelElement(2, "models/b.vmdl"));

            var randomResult = SmartPropEvaluator.Evaluate(Root(random));
            await Assert.That(randomResult.Models).HasSingleItem();
            await Assert.That(randomResult.Models[0].ModelName).IsEqualTo("models/a.vmdl");

            random["m_SelectionMode"] = new KVObject("SPECIFIC");
            random["m_SpecificChildIndex"] = new KVObject(99);
            var clamped = SmartPropEvaluator.Evaluate(Root(random));
            await Assert.That(clamped.Models[0].ModelName).IsEqualTo("models/b.vmdl");
        }

    }
}
