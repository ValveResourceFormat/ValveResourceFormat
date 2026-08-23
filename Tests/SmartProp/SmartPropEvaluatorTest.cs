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
        public async Task PickOneLiveSelectionOverridesTheAuthoredChoice()
        {
            var pickOne = Element("PickOne", 10,
                ModelElement(1, "models/a.vmdl"),
                ModelElement(2, "models/b.vmdl"),
                ModelElement(3, "models/c.vmdl"));
            pickOne["m_SelectionMode"] = new KVObject("SPECIFIC");
            pickOne["m_SpecificChildIndex"] = new KVObject(0);

            var context = new SmartPropEvaluationContext(pickOneSelections: new Dictionary<int, int> { [10] = 2 });
            var result = SmartPropEvaluator.Evaluate(Root(pickOne), context);

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
        public async Task FitOnLineSizerUpdatesEndCapsAndRepeatingMiddleParts()
        {
            var front = ModelElement(1, "models/front.vmdl");
            front["m_SelectionCriteria"] = ArrayOf(
                Criteria("LinearLength", ("m_flLength", new KVObject(120f))),
                Criteria("EndCap", ("m_bStart", new KVObject(true)), ("m_bEnd", new KVObject(false))));
            var middle = ModelElement(2, "models/middle.vmdl");
            middle["m_SelectionCriteria"] = ArrayOf(Criteria("LinearLength", ("m_flLength", new KVObject(80f))));
            var back = ModelElement(3, "models/back.vmdl");
            back["m_SelectionCriteria"] = ArrayOf(
                Criteria("LinearLength", ("m_flLength", new KVObject(120f))),
                Criteria("EndCap", ("m_bStart", new KVObject(false)), ("m_bEnd", new KVObject(true))));

            var length = KVObject.Collection();
            length["m_SourceName"] = new KVObject("length");
            var end = KVObject.Collection();
            end["m_Components"] = ArrayOf(length, new KVObject(0f), new KVObject(0f));
            var fitOnLine = WithModifiers(Element("FitOnLine", 20, front, middle, back),
                Modifier("CreateSizer", ("m_flInitialMaxX", new KVObject(448f)), ("m_OutputVariableMaxX", new KVObject("length"))));
            fitOnLine["m_vEnd"] = end;

            var context = new SmartPropEvaluationContext(widgetOutputValues: new Dictionary<string, float> { ["length"] = 400f });
            var result = SmartPropEvaluator.Evaluate(Root(fitOnLine), context);

            await Assert.That(result.Models).Count().IsEqualTo(4);
            await Assert.That(result.Models[0].Position.X).IsEqualTo(0f);
            await Assert.That(result.Models[1].Position.X).IsEqualTo(120f);
            await Assert.That(result.Models[2].Position.X).IsEqualTo(200f);
            await Assert.That(result.Models[3].Position.X).IsEqualTo(400f);
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

        [Test]
        public async Task NestedSmartPropsEvaluateThroughResolver()
        {
            var nestedRoot = Root(ModelElement(5, "models/nested.vmdl"));

            var container = Element("SmartProp", 40);
            container["m_sSmartProp"] = new KVObject("nested.vsmart");

            var result = SmartPropEvaluator.Evaluate(
                Root(container),
                nestedPropResolver: path => path == "nested.vsmart" ? nestedRoot : null);

            await Assert.That(result.Models).HasSingleItem();
            await Assert.That(result.Models[0].ModelName).IsEqualTo("models/nested.vmdl");
        }

        [Test]
        public async Task NestedSmartPropTransformsCombineWithParent()
        {
            var nestedRoot = Root(ModelElement(5, "models/nested.vmdl"));

            var container = WithModifiers(Element("SmartProp", 40),
                Modifier("Translate", ("m_vPosition", Vec(0f, 0f, 200f))));
            container["m_sSmartProp"] = new KVObject("nested.vsmart");

            var result = SmartPropEvaluator.Evaluate(
                Root(container),
                nestedPropResolver: _ => nestedRoot);

            await Assert.That(result.Models[0].Position.Z).IsEqualTo(200f).Within(Tolerance);
        }

        [Test]
        public async Task SelfReferencingSmartPropTerminates()
        {
            KVObject? container = null;
            container = WithModifiers(Element("SmartProp", 40));
            container["m_sSmartProp"] = new KVObject("loop.vsmart");

            KVObject? Resolver(string path) => path == "loop.vsmart" ? Root(container!) : null;

            var result = SmartPropEvaluator.Evaluate(Root(container!), nestedPropResolver: Resolver);
            await Assert.That(result.Models).IsEmpty();
        }

        [Test]
        public async Task NestedSmartPropWithoutResolverIsSkipped()
        {
            var container = Element("SmartProp", 40);
            container["m_sSmartProp"] = new KVObject("nested.vsmart");

            var result = SmartPropEvaluator.Evaluate(Root(container));
            await Assert.That(result.Models).IsEmpty();
        }

        [Test]
        public async Task VariablesReachExpressionsThroughTheTree()
        {
            var root = Root(ModelElement(1, "models/a.vmdl"));
            var variables = KVObject.Array();
            var variable = KVObject.Collection();
            variable["generic_data_type"] = new KVObject("CSmartPropVariable_Float");
            variable["m_VariableName"] = new KVObject("lift");
            variable["m_DefaultValue"] = new KVObject(15f);
            variables.Add(variable);
            root["m_Variables"] = variables;

            var binding = KVObject.Collection();
            binding["m_Expression"] = new KVObject("lift * 2");
            root["m_Children"] = ArrayOf(
                ModelElement(1, "models/a.vmdl", Modifier("Translate", ("m_vPosition", binding))));

            var result = SmartPropEvaluator.Evaluate(root);
            await Assert.That(result.Models[0].Position.Z).IsEqualTo(30f).Within(Tolerance);
        }

        [Test]
        public async Task EmptyRootYieldsEmptyResult()
        {
            var result = SmartPropEvaluator.Evaluate(Root());
            await Assert.That(result.Models).IsEmpty();
            await Assert.That(result.Widgets).IsEmpty();
            await Assert.That(result.Paths).IsEmpty();
        }

        [Test]
        public async Task EvaluationLimitsModelResults()
        {
            var result = SmartPropEvaluator.Evaluate(
                Root(
                    ModelElement(1, "models/a.vmdl"),
                    ModelElement(2, "models/b.vmdl"),
                    ModelElement(3, "models/c.vmdl")),
                maxModels: 2);

            await Assert.That(result.Models.Count).IsEqualTo(2);
        }

        [Test]
        public async Task EvaluationLimitsPathInstances()
        {
            var path = Element("PlaceOnPath", 30, ModelElement(1, "models/a.vmdl"));
            path["m_DefaultPath"] = ArrayOf(PathPoint(0f, 0f, 0f), PathPoint(1000f, 0f, 0f));
            path["m_flSpacing"] = new KVObject(0.001f);
            path["m_PathSpace"] = new KVObject("WORLD");

            var result = SmartPropEvaluator.Evaluate(Root(path), maxPathInstances: 4);

            await Assert.That(result.Models.Count).IsEqualTo(4);
        }

        [Test]
        public async Task EvaluatesMaterialGroupAndTintColor()
        {
            var root = Root();
            var variables = KVObject.Array();

            var varMat = KVObject.Collection();
            varMat["_class"] = new KVObject("CSmartPropVariable_MaterialGroup");
            varMat["m_VariableName"] = new KVObject("Glow_Amount");
            varMat["m_DefaultValue"] = new KVObject("on");
            variables.Add(varMat);

            var varColor = KVObject.Collection();
            varColor["_class"] = new KVObject("CSmartPropVariable_Color");
            varColor["m_VariableName"] = new KVObject("Tint_Color");
            var colArr = KVObject.Array();
            colArr.Add(new KVObject(159));
            colArr.Add(new KVObject(135));
            colArr.Add(new KVObject(43));
            varColor["m_DefaultValue"] = colArr;
            variables.Add(varColor);

            root["m_Variables"] = variables;

            var model = ModelElement(10, "models/floodlight.vmdl");
            var matBinding = KVObject.Collection();
            matBinding["m_SourceName"] = new KVObject("Glow_Amount");
            model["m_MaterialGroupName"] = matBinding;

            var tintOp = KVObject.Collection();
            tintOp["_class"] = new KVObject("CSmartPropOperation_SetTintColor");
            var choices = KVObject.Array();
            var choice = KVObject.Collection();
            var cBind = KVObject.Collection();
            cBind["m_SourceName"] = new KVObject("Tint_Color");
            choice["m_Color"] = cBind;
            choices.Add(choice);
            tintOp["m_ColorChoices"] = choices;

            var modArray = KVObject.Array();
            modArray.Add(tintOp);
            model["m_Modifiers"] = modArray;

            root["m_Children"] = ArrayOf(model);

            var result = SmartPropEvaluator.Evaluate(root);
            await Assert.That(result.Models.Count).IsEqualTo(1);
            var evalModel = result.Models[0];
            await Assert.That(evalModel.MaterialGroup).IsEqualTo("on");
            await Assert.That(evalModel.TintColor).IsNotNull();
            await Assert.That(evalModel.TintColor!.Value.X).IsEqualTo(159f / 255f).Within(Tolerance);
            await Assert.That(evalModel.TintColor!.Value.Y).IsEqualTo(135f / 255f).Within(Tolerance);
            await Assert.That(evalModel.TintColor!.Value.Z).IsEqualTo(43f / 255f).Within(Tolerance);
        }

        [Test]
        public async Task EvaluatesFloodlightSmartPropWithFiltersAndVariables()
        {
            var root = Root();
            var vars = KVObject.Array();

            var varStyle = KVObject.Collection();
            varStyle["_class"] = new KVObject("CSmartPropVariable_Int");
            varStyle["m_VariableName"] = new KVObject("Floodlight_Style");
            varStyle["m_DefaultValue"] = new KVObject(1);
            vars.Add(varStyle);
            root["m_Variables"] = vars;

            // Group 1 active when Floodlight_Style == 1
            var filter1 = KVObject.Collection();
            filter1["_class"] = new KVObject("CSmartPropFilter_VariableValue");
            var comp1 = KVObject.Collection();
            comp1["m_Name"] = new KVObject("Floodlight_Style");
            comp1["m_Value"] = new KVObject(1);
            comp1["m_Comparison"] = new KVObject("EQUAL");
            filter1["m_VariableComparison"] = comp1;

            var group1 = Element("Group", 20, ModelElement(21, "models/floodlight_style1.vmdl"));
            group1["m_Modifiers"] = ArrayOf(filter1);

            // Group 2 active when Floodlight_Style == 2
            var filter2 = KVObject.Collection();
            filter2["_class"] = new KVObject("CSmartPropFilter_VariableValue");
            var comp2 = KVObject.Collection();
            comp2["m_Name"] = new KVObject("Floodlight_Style");
            comp2["m_Value"] = new KVObject(2);
            comp2["m_Comparison"] = new KVObject("EQUAL");
            filter2["m_VariableComparison"] = comp2;

            var group2 = Element("Group", 30, ModelElement(31, "models/floodlight_style2.vmdl"));
            group2["m_Modifiers"] = ArrayOf(filter2);

            root["m_Children"] = ArrayOf(group1, group2);

            // Evaluate with style 1
            var ctx1 = new SmartPropEvaluationContext(new Dictionary<string, object?> { ["Floodlight_Style"] = 1 });
            var result1 = SmartPropEvaluator.Evaluate(root, ctx1);
            await Assert.That(result1.Models.Count).IsEqualTo(1);
            await Assert.That(result1.Models[0].ModelName).IsEqualTo("models/floodlight_style1.vmdl");

            // Evaluate with style 2
            var ctx2 = new SmartPropEvaluationContext(new Dictionary<string, object?> { ["Floodlight_Style"] = 2 });
            var result2 = SmartPropEvaluator.Evaluate(root, ctx2);
            await Assert.That(result2.Models.Count).IsEqualTo(1);
            await Assert.That(result2.Models[0].ModelName).IsEqualTo("models/floodlight_style2.vmdl");
        }
    }
}
