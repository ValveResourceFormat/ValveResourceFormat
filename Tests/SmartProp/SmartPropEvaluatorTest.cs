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

        [Test]
        public async Task PickOneEmitsHandleWidget()
        {
            var pickOne = Element("PickOne", 10, ModelElement(1, "models/a.vmdl"));
            pickOne["m_OutputChoiceVariableName"] = new KVObject("choice");

            var result = SmartPropEvaluator.Evaluate(Root(pickOne));
            await Assert.That(result.Widgets).HasSingleItem();
            await Assert.That(result.Widgets[0]).IsTypeOf<SmartPropPickOneHandleWidget>();
        }

        [Test]
        public async Task FitOnLineExposesLinearLengthRatioToScaleModifiers()
        {
            // Children consume the derived linear scale through LinearScale() in their
            // own modifiers; a 100 unit line over a 50 unit child yields a factor of 2
            var scaleBinding = KVObject.Collection();
            scaleBinding["m_Expression"] = new KVObject("LinearScale()");

            var child = ModelElement(1, "models/a.vmdl",
                Modifier("Scale", ("m_flScale", new KVObject(1f))));
            child["m_Modifiers"] = ArrayOf(Modifier("Scale", ("m_flScale", scaleBinding)));
            var criteriaList = KVObject.Array();
            criteriaList.Add(Criteria("LinearLength",
                ("m_flLength", new KVObject(50f)),
                ("m_bAllowScale", new KVObject(true))));
            child["m_SelectionCriteria"] = criteriaList;

            var fitOnLine = Element("FitOnLine", 20, child);
            fitOnLine["m_vStart"] = Vec(0f, 0f, 0f);
            fitOnLine["m_vEnd"] = Vec(100f, 0f, 0f);

            var result = SmartPropEvaluator.Evaluate(Root(fitOnLine));
            await Assert.That(result.Models[0].Scale.X).IsEqualTo(2f).Within(Tolerance);
        }

        [Test]
        public async Task FitOnLineExposesLinearScaleToExpressions()
        {
            // The child reads LinearScale() through an expression-bound translate
            var binding = KVObject.Collection();
            binding["m_Expression"] = new KVObject("LinearScale() * 10");

            var child = ModelElement(1, "models/a.vmdl",
                Modifier("Translate", ("m_vPosition", binding)));
            var criteriaList = KVObject.Array();
            criteriaList.Add(Criteria("LinearLength", ("m_flLength", new KVObject(100f))));
            child["m_SelectionCriteria"] = criteriaList;

            var fitOnLine = Element("FitOnLine", 20, child);
            fitOnLine["m_vStart"] = Vec(0f, 0f, 0f);
            fitOnLine["m_vEnd"] = Vec(250f, 0f, 0f);

            var result = SmartPropEvaluator.Evaluate(Root(fitOnLine));
            await Assert.That(result.Models[0].Position.X).IsEqualTo(25f).Within(Tolerance);
        }

        [Test]
        public async Task PlaceOnPathSpawnsInstancesAlongTheCurve()
        {
            // A straight 400 unit path along X with spacing 100 yields 5 instances
            var path = Element("PlaceOnPath", 30, ModelElement(1, "models/a.vmdl"));
            var points = KVObject.Array();
            points.Add(PathPoint(0f, 0f, 0f));
            points.Add(PathPoint(400f, 0f, 0f));
            path["m_DefaultPath"] = points;
            path["m_flSpacing"] = new KVObject(100f);
            path["m_PathSpace"] = new KVObject("WORLD");

            var result = SmartPropEvaluator.Evaluate(Root(path));

            await Assert.That(result.Models.Count).IsEqualTo(5);
            await Assert.That(result.Paths).HasSingleItem();
            await Assert.That(result.Paths[0].ControlPoints.Length).IsEqualTo(2);

            // Instances sit at 0, 100, 200, 300, 400 along X
            for (var i = 0; i < 5; i++)
            {
                await Assert.That(result.Models[i].Position.X).IsEqualTo(i * 100f).Within(Tolerance);
            }
        }

        [Test]
        public async Task PlaceOnPathFiltersChildrenByCriteria()
        {
            var capOnly = ModelElement(1, "models/cap.vmdl");
            var criteriaList = KVObject.Array();
            criteriaList.Add(Criteria("PathPosition", ("m_PlaceAtPositions", new KVObject("START_AND_END"))));
            capOnly["m_SelectionCriteria"] = criteriaList;

            var everywhere = ModelElement(2, "models/mid.vmdl");

            var path = Element("PlaceOnPath", 30, capOnly, everywhere);
            var points = KVObject.Array();
            points.Add(PathPoint(0f, 0f, 0f));
            points.Add(PathPoint(300f, 0f, 0f));
            path["m_DefaultPath"] = points;
            path["m_flSpacing"] = new KVObject(100f);
            path["m_PathSpace"] = new KVObject("WORLD");

            var result = SmartPropEvaluator.Evaluate(Root(path));

            // The cap child appears only at the two ends, the mid child at all four spots
            var caps = result.Models.Where(m => m.ModelName == "models/cap.vmdl").ToArray();
            var mids = result.Models.Where(m => m.ModelName == "models/mid.vmdl").ToArray();
            await Assert.That(caps.Length).IsEqualTo(2);
            await Assert.That(mids.Length).IsEqualTo(4);
            await Assert.That(caps[0].Position.X).IsEqualTo(0f).Within(Tolerance);
            await Assert.That(caps[1].Position.X).IsEqualTo(300f).Within(Tolerance);
        }

        [Test]
        public async Task PlaceOnPathOffsetAlongPathShiftsFirstInstance()
        {
            var path = Element("PlaceOnPath", 30, ModelElement(1, "models/a.vmdl"));
            var points = KVObject.Array();
            points.Add(PathPoint(0f, 0f, 0f));
            points.Add(PathPoint(100f, 0f, 0f));
            path["m_DefaultPath"] = points;
            path["m_flSpacing"] = new KVObject(50f);
            path["m_flOffsetAlongPath"] = new KVObject(25f);
            path["m_PathSpace"] = new KVObject("WORLD");

            var result = SmartPropEvaluator.Evaluate(Root(path));
            await Assert.That(result.Models[0].Position.X).IsEqualTo(25f).Within(Tolerance);
            await Assert.That(result.Models[1].Position.X).IsEqualTo(75f).Within(Tolerance);
            await Assert.That(result.Models.Count).IsEqualTo(2);
        }

    }
}
