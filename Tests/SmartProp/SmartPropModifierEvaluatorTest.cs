using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropModifierEvaluatorTest
    {
        private const float Tolerance = 1e-3f;

        private static SmartPropEvaluationContext Context() => new();

        private static KVObject Element(string className, params KVObject[] modifiers)
        {
            var element = KVObject.Collection();
            element["generic_data_type"] = new KVObject($"CSmartPropElement_{className}");
            var list = KVObject.Array();
            foreach (var modifier in modifiers)
            {
                list.Add(modifier);
            }

            element["m_Modifiers"] = list;
            element["m_nElementID"] = new KVObject(7);
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

        private static Vector3 Translation(Matrix4x4 matrix) => new(matrix.M41, matrix.M42, matrix.M43);

        private static Vector3 Row(Matrix4x4 matrix, int row) => row switch
        {
            0 => new(matrix.M11, matrix.M12, matrix.M13),
            1 => new(matrix.M21, matrix.M22, matrix.M23),
            _ => new(matrix.M31, matrix.M32, matrix.M33),
        };

        [Test]
        public async Task GetClassNameStripsKnownPrefixes()
        {
            var node = KVObject.Collection();
            node["generic_data_type"] = new KVObject("CSmartPropOperation_Translate");
            await Assert.That(SmartPropModifierEvaluator.GetClassName(node)).IsEqualTo("Translate");

            node["generic_data_type"] = new KVObject("CSmartPropElement_PickOne");
            await Assert.That(SmartPropModifierEvaluator.GetClassName(node)).IsEqualTo("PickOne");

            node["generic_data_type"] = new KVObject("PlainName");
            await Assert.That(SmartPropModifierEvaluator.GetClassName(node)).IsEqualTo("PlainName");

            var empty = KVObject.Collection();
            await Assert.That(SmartPropModifierEvaluator.GetClassName(empty)).IsEmpty();
        }

        [Test]
        public async Task TranslateInElementSpaceOffsetsLocalTranslation()
        {
            var element = Element("Group", Modifier("Translate", ("m_vPosition", Vec(10f, 20f, 30f))));
            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(element, Context());

            await Assert.That(Translation(result.LocalMatrix)).IsEqualTo(new Vector3(10f, 20f, 30f));
            await Assert.That(result.WorldMatrix).IsEqualTo(result.LocalMatrix);
            await Assert.That(result.ModelWorldMatrix).IsEqualTo(result.WorldMatrix);
        }

        [Test]
        public async Task TranslateInWorldSpacePostMultiplies()
        {
            // Start from a yaw-90 rotated local matrix, then translate in world space
            var rotate = Modifier("Rotate", ("m_vRotation", Vec(0f, 90f, 0f)));
            var translate = Modifier("Translate",
                ("m_vPosition", Vec(10f, 0f, 0f)),
                ("m_CoordinateSpace", new KVObject("WORLD")));

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", rotate, translate), Context());

            // World space translate after rotation: local @ T moves the origin along world X
            // regardless of the accumulated rotation
            await Assert.That(Translation(result.LocalMatrix)).IsEqualTo(new Vector3(10f, 0f, 0f));

            // The forward row still points along +Y from the yaw
            await Assert.That(Vector3.Distance(Row(result.LocalMatrix, 0), new Vector3(0f, 1f, 0f))).IsLessThan(Tolerance);
        }

        [Test]
        public async Task TranslateInElementSpaceMovesAlongRotatedAxes()
        {
            var rotate = Modifier("Rotate", ("m_vRotation", Vec(0f, 90f, 0f)));
            var translate = Modifier("Translate", ("m_vPosition", Vec(10f, 0f, 0f)));

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", rotate, translate), Context());

            // Element space translate pre-multiplies: the +X offset rotates to +Y
            await Assert.That(Vector3.Distance(Translation(result.LocalMatrix), new Vector3(0f, 10f, 0f))).IsLessThan(Tolerance);
        }

        [Test]
        public async Task SetPositionOverridesTranslation()
        {
            var first = Modifier("Translate", ("m_vPosition", Vec(50f, 0f, 0f)));
            var setPosition = Modifier("SetPosition", ("m_vPosition", Vec(1f, 2f, 3f)));

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", first, setPosition), Context());

            await Assert.That(Translation(result.LocalMatrix)).IsEqualTo(new Vector3(1f, 2f, 3f));
        }

        [Test]
        public async Task SetOrientationBuildsFrameFromForwardAndUp()
        {
            var modifier = Modifier("SetOrientation",
                ("m_vForwardVector", Vec(0f, 1f, 0f)),
                ("m_vUpVector", Vec(0f, 0f, 1f)));

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", modifier), Context());

            // Forward lands on the first row
            await Assert.That(Vector3.Distance(Row(result.LocalMatrix, 0), new Vector3(0f, 1f, 0f))).IsLessThan(Tolerance);
        }

        [Test]
        public async Task ResetRotationKeepsPositionAndScale()
        {
            var scale = Modifier("Scale", ("m_flScale", new KVObject(2f)));
            var rotate = Modifier("Rotate", ("m_vRotation", Vec(30f, 45f, 10f)));
            var move = Modifier("Translate", ("m_vPosition", Vec(5f, 6f, 7f)));
            var reset = Modifier("ResetRotation");

            var before = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", scale, rotate, move), Context());
            var after = SmartPropModifierEvaluator.EvaluateElementModifiers(
                Element("Group", scale, rotate, move, reset), Context());

            // Resetting rotation preserves the decomposed position and scale components
            // of the active state, only the angles change
            var (positionBefore, anglesBefore, decomposedScale) = SmartPropTransform.DecomposeTRS(before.LocalMatrix);
            var (positionAfter, anglesAfter, scaleAfter) = SmartPropTransform.DecomposeTRS(after.LocalMatrix);

            await Assert.That(Vector3.Distance(positionAfter, positionBefore)).IsLessThan(Tolerance);
            await Assert.That(Vector3.Distance(scaleAfter, decomposedScale)).IsLessThan(Tolerance);
            await Assert.That(anglesBefore.X).IsEqualTo(30f).Within(Tolerance);
            await Assert.That(Vector3.Distance(anglesAfter, Vector3.Zero)).IsLessThan(Tolerance);
        }

        [Test]
        public async Task ResetRotationCanKeepSelectedAxes()
        {
            var rotate = Modifier("Rotate", ("m_vRotation", Vec(30f, 45f, 10f)));
            var reset = Modifier("ResetRotation",
                ("m_bResetPitch", new KVObject(true)),
                ("m_bResetYaw", new KVObject(false)),
                ("m_bResetRoll", new KVObject(false)));

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", rotate, reset), Context());

            var (_, angles, _) = SmartPropTransform.DecomposeTRS(result.LocalMatrix);
            await Assert.That(MathF.Abs(angles.X)).IsLessThan(Tolerance);
            await Assert.That(MathF.Abs(angles.Y - 45f)).IsLessThan(Tolerance);
            await Assert.That(MathF.Abs(angles.Z - 10f)).IsLessThan(Tolerance);
        }

        [Test]
        public async Task ScaleSupportsUniformAndPerAxis()
        {
            var uniform = Modifier("Scale", ("m_flScale", new KVObject(3f)));
            var perAxis = Modifier("Scale", ("m_vScale", Vec(1f, 2f, 4f)));
            var reset = Modifier("ResetScale");

            var scaled = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", uniform, perAxis), Context());
            var (_, _, scale) = SmartPropTransform.DecomposeTRS(scaled.LocalMatrix);
            await Assert.That(Vector3.Distance(scale, new Vector3(3f, 6f, 12f))).IsLessThan(Tolerance);

            var resetResult = SmartPropModifierEvaluator.EvaluateElementModifiers(
                Element("Group", uniform, perAxis, reset), Context());
            var (_, _, resetScale) = SmartPropTransform.DecomposeTRS(resetResult.LocalMatrix);
            await Assert.That(Vector3.Distance(resetScale, Vector3.One)).IsLessThan(Tolerance);
        }

        [Test]
        public async Task RandomModifiersAreDeterministicAndBounded()
        {
            var offset = Modifier("RandomOffset",
                ("m_vRandomPositionMin", Vec(0f, 10f, -5f)),
                ("m_vRandomPositionMax", Vec(2f, 20f, 5f)));
            var rotation = Modifier("RandomRotation",
                ("m_vRandomRotationMin", Vec(0f, 0f, 0f)),
                ("m_vRandomRotationMax", Vec(90f, 90f, 90f)));
            var scale = Modifier("RandomScale",
                ("m_flRandomScaleMin", new KVObject(0.5f)),
                ("m_flRandomScaleMax", new KVObject(1.5f)));

            var first = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", offset, rotation, scale), Context());
            var second = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", offset, rotation, scale), Context());

            await Assert.That(first.LocalMatrix).IsEqualTo(second.LocalMatrix);

            var (position, angles, scaleVector) = SmartPropTransform.DecomposeTRS(first.LocalMatrix);
            await Assert.That(position.X).IsGreaterThanOrEqualTo(0f);
            await Assert.That(position.X).IsLessThanOrEqualTo(2f);
            await Assert.That(position.Y).IsGreaterThanOrEqualTo(10f);
            await Assert.That(position.Y).IsLessThanOrEqualTo(20f);
            await Assert.That(position.Z).IsGreaterThanOrEqualTo(-5f);
            await Assert.That(position.Z).IsLessThanOrEqualTo(5f);
            foreach (var angle in (float[])[angles.X, angles.Y, angles.Z])
            {
                await Assert.That(angle).IsGreaterThanOrEqualTo(0f);
                await Assert.That(angle).IsLessThanOrEqualTo(90f);
            }

            await Assert.That(scaleVector.X).IsGreaterThanOrEqualTo(0.5f);
            await Assert.That(scaleVector.X).IsLessThanOrEqualTo(1.5f);
        }

        [Test]
        public async Task RandomModifiersVaryByElementIdAndInstance()
        {
            var offset = Modifier("RandomOffset",
                ("m_vRandomPositionMin", Vec(0f, 0f, 0f)),
                ("m_vRandomPositionMax", Vec(100f, 100f, 100f)));

            var elementA = Element("Group", offset);
            elementA["m_nElementID"] = new KVObject(1);
            var elementB = Element("Group", offset);
            elementB["m_nElementID"] = new KVObject(2);

            var byElementA = SmartPropModifierEvaluator.EvaluateElementModifiers(elementA, Context());
            var byElementB = SmartPropModifierEvaluator.EvaluateElementModifiers(elementB, Context());
            var byInstance = SmartPropModifierEvaluator.EvaluateElementModifiers(elementA, new SmartPropEvaluationContext(instanceIndex: 5));

            await Assert.That(Translation(byElementA.LocalMatrix)).IsNotEqualTo(Translation(byElementB.LocalMatrix));
            await Assert.That(Translation(byElementA.LocalMatrix)).IsNotEqualTo(Translation(byInstance.LocalMatrix));
        }

        [Test]
        public async Task SaveAndRestoreStateRoundTripsTransform()
        {
            var save = Modifier("SaveState", ("m_StateName", new KVObject("corner")));
            var moveAway = Modifier("Translate", ("m_vPosition", Vec(99f, 99f, 99f)));
            var restore = Modifier("RestoreState", ("m_StateName", new KVObject("corner")));

            var withRestore = SmartPropModifierEvaluator.EvaluateElementModifiers(
                Element("Group", save, moveAway, restore), Context(), stateMap: []);
            await Assert.That(Translation(withRestore.LocalMatrix)).IsEqualTo(Vector3.Zero);

            // Without the restore, the move sticks
            var withoutRestore = SmartPropModifierEvaluator.EvaluateElementModifiers(
                Element("Group", save, moveAway), Context(), stateMap: []);
            await Assert.That(Translation(withoutRestore.LocalMatrix)).IsEqualTo(new Vector3(99f, 99f, 99f));

            // Restoring an unknown state leaves the matrix alone
            var unknown = SmartPropModifierEvaluator.EvaluateElementModifiers(
                Element("Group", moveAway, restore), Context(), stateMap: []);
            await Assert.That(Translation(unknown.LocalMatrix)).IsEqualTo(new Vector3(99f, 99f, 99f));
        }

        [Test]
        public async Task SetVariableWritesContextOverride()
        {
            var variableValue = KVObject.Collection();
            variableValue["m_TargetName"] = new KVObject("height");
            variableValue["m_Value"] = new KVObject("InstanceIndex() * 10");

            var setVariable = Modifier("SetVariable", ("m_VariableValue", variableValue));
            var translate = Modifier("Translate", ("m_vPosition", Vec(0f, 0f, 0f)));
            translate["m_vPosition"] = Binding("m_SourceName", "height_z");

            var context = new SmartPropEvaluationContext(instanceIndex: 3);
            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(
                Element("Group", setVariable, translate), context);

            // The override holds the raw expression string, which resolves through the
            // context's variable machinery
            await Assert.That(context.GetVariable("height")).IsEqualTo("InstanceIndex() * 10");
            _ = result;
        }

        [Test]
        public async Task SetVariableFlatFormWritesOverride()
        {
            var setVariable = Modifier("SetVariableFloat",
                ("m_VariableName", new KVObject("width")),
                ("m_flValue", new KVObject(42f)));

            var context = Context();
            _ = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", setVariable), context);

            await Assert.That(context.GetVariable("width")).IsEqualTo(42f);
        }

        [Test]
        public async Task DisabledModifierIsSkipped()
        {
            var disabled = Modifier("Translate",
                ("m_vPosition", Vec(50f, 0f, 0f)),
                ("m_bEnabled", new KVObject(false)));

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", disabled), Context());
            await Assert.That(Translation(result.LocalMatrix)).IsEqualTo(Vector3.Zero);
        }

        [Test]
        public async Task UnknownModifierLeavesTransformAlone()
        {
            var unknown = Modifier("MaterialOverride");
            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", unknown), Context());

            await Assert.That(result.LocalMatrix).IsEqualTo(Matrix4x4.Identity);
            await Assert.That(result.Widgets).IsEmpty();
        }

        [Test]
        public async Task CreateLocatorEmitsWorldPositionedWidget()
        {
            var parent = Matrix4x4.CreateTranslation(new Vector3(100f, 0f, 0f));
            var move = Modifier("Translate", ("m_vPosition", Vec(0f, 10f, 0f)));
            var locator = Modifier("CreateLocator",
                ("m_LocatorName", new KVObject("corner")),
                ("m_vOffset", Vec(0f, 0f, 5f)),
                ("m_flDisplayScale", new KVObject(2.5f)));

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(
                Element("Group", move, locator), Context(), parent);

            var widget = (SmartPropLocatorWidget)result.Widgets[0];
            await Assert.That(result.Widgets).HasSingleItem();
            await Assert.That(widget.Name).IsEqualTo("corner");
            await Assert.That(widget.DisplayScale).IsEqualTo(2.5f);
            await Assert.That(widget.ElementId).IsEqualTo(7);

            // Offset transformed by the widget-time world matrix: local (0,10,0) plus
            // offset (0,0,5) then shifted by the parent
            await Assert.That(widget.Position).IsEqualTo(new Vector3(100f, 10f, 5f));
        }

        [Test]
        public async Task CreateRotatorRotatesElementSpaceAxis()
        {
            var rotate = Modifier("Rotate", ("m_vRotation", Vec(0f, 90f, 0f)));
            var rotator = Modifier("CreateRotator",
                ("m_vRotationAxis", Vec(1f, 0f, 0f)),
                ("m_CoordinateSpace", new KVObject("ELEMENT")),
                ("m_flDisplayRadius", new KVObject(32f)),
                ("m_flInitialAngle", new KVObject(45f)),
                ("m_DisplayColor", Vec(255f, 128f, 0f)));

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(
                Element("Group", rotate, rotator), Context());

            var widget = (SmartPropRotatorWidget)result.Widgets[0];
            await Assert.That(widget.Radius).IsEqualTo(32f);
            await Assert.That(widget.Angle).IsEqualTo(45f);

            // Element space +X axis rotates to +Y under the yaw-90 frame
            await Assert.That(Vector3.Distance(widget.Axis, new Vector3(0f, 1f, 0f))).IsLessThan(Tolerance);

            // 0-255 colors rescale into 0-1
            await Assert.That(Vector3.Distance(widget.Color, new Vector3(1f, 128f / 255f, 0f))).IsLessThan(Tolerance);
        }

        [Test]
        public async Task CreateSizerOnlyEmitsWhenAnAxisIsActive()
        {
            var inactive = Modifier("CreateSizer");
            var active = Modifier("CreateSizer",
                ("m_flInitialMinX", new KVObject(-10f)),
                ("m_flInitialMaxX", new KVObject(10f)),
                ("m_OutputVariableMinY", new KVObject("sizer_min_y")));

            var none = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", inactive), Context());
            await Assert.That(none.Widgets).IsEmpty();

            var some = SmartPropModifierEvaluator.EvaluateElementModifiers(Element("Group", active), Context());
            var widget = (SmartPropSizerWidget)some.Widgets[0];
            await Assert.That(widget.MinBounds).IsEqualTo(new Vector3(-10f, 0f, 0f));
            await Assert.That(widget.MaxBounds.X).IsEqualTo(10f);
            await Assert.That(widget.ActiveAxes.X).IsTrue();
            await Assert.That(widget.ActiveAxes.Y).IsTrue();
            await Assert.That(widget.ActiveAxes.Z).IsFalse();
            await Assert.That(widget.Handles.MinY).IsTrue();
            await Assert.That(widget.Handles.MaxX).IsFalse();
        }

        [Test]
        public async Task PickOneElementEmitsHandleWidget()
        {
            var element = Element("PickOne");
            element["m_vHandleOffset"] = Vec(0f, 4f, 0f);
            element["m_HandleSize"] = new KVObject(12f);
            element["m_HandleShape"] = new KVObject("diamond");
            element["m_OutputChoiceVariableName"] = new KVObject("picked");

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(element, Context());

            var handle = (SmartPropPickOneHandleWidget)result.Widgets[0];
            await Assert.That(handle.Size).IsEqualTo(12f);
            await Assert.That(handle.Shape).IsEqualTo("DIAMOND");
            await Assert.That(handle.Name).IsEqualTo("picked");
            await Assert.That(handle.Position).IsEqualTo(new Vector3(0f, 4f, 0f));
        }

        [Test]
        public async Task PickOneHandleReadsTypoOffsetField()
        {
            var element = Element("PickOne");
            element["m_vHandleOfffset"] = Vec(7f, 0f, 0f);

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(element, Context());
            var handle = (SmartPropPickOneHandleWidget)result.Widgets[0];
            await Assert.That(handle.Offset).IsEqualTo(new Vector3(7f, 0f, 0f));
        }

        [Test]
        public async Task ModelScaleOnlyAffectsModelWorldMatrix()
        {
            var uniform = Element("Model", Modifier("Translate", ("m_vPosition", Vec(1f, 0f, 0f))));
            uniform["m_flUniformModelScale"] = new KVObject(4f);

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(uniform, Context());

            var (_, _, worldScale) = SmartPropTransform.DecomposeTRS(result.WorldMatrix);
            var (_, _, modelScale) = SmartPropTransform.DecomposeTRS(result.ModelWorldMatrix);
            await Assert.That(Vector3.Distance(worldScale, Vector3.One)).IsLessThan(Tolerance);
            await Assert.That(Vector3.Distance(modelScale, new Vector3(4f, 4f, 4f))).IsLessThan(Tolerance);
        }

        [Test]
        public async Task FilterVariableValuePrunesElementWhenConditionFalse()
        {
            var filter = KVObject.Collection();
            filter["generic_data_type"] = new KVObject("CSmartPropFilter_VariableValue");
            var comp = KVObject.Collection();
            comp["m_Name"] = new KVObject("Floodlight_Style");
            comp["m_Value"] = new KVObject(1);
            comp["m_Comparison"] = new KVObject("EQUAL");
            filter["m_VariableComparison"] = comp;

            var element = Element("Model", filter);

            var ctx1 = new SmartPropEvaluationContext(new Dictionary<string, object?> { ["Floodlight_Style"] = 1 });
            var result1 = SmartPropModifierEvaluator.EvaluateElementModifiers(element, ctx1);
            await Assert.That(result1.IsFilteredOut).IsFalse();

            var ctx2 = new SmartPropEvaluationContext(new Dictionary<string, object?> { ["Floodlight_Style"] = 2 });
            var result2 = SmartPropModifierEvaluator.EvaluateElementModifiers(element, ctx2);
            await Assert.That(result2.IsFilteredOut).IsTrue();
        }

        [Test]
        public async Task FilterVariableValueSupportsComparisons()
        {
            var filter = KVObject.Collection();
            filter["generic_data_type"] = new KVObject("CSmartPropFilter_VariableValue");
            var comp = KVObject.Collection();
            comp["m_Name"] = new KVObject("Count");
            comp["m_Value"] = new KVObject(5);
            comp["m_Comparison"] = new KVObject("GREATER_OR_EQUAL");
            filter["m_VariableComparison"] = comp;

            var element = Element("Model", filter);

            var ctxPassing = new SmartPropEvaluationContext(new Dictionary<string, object?> { ["Count"] = 5 });
            await Assert.That(SmartPropModifierEvaluator.EvaluateElementModifiers(element, ctxPassing).IsFilteredOut).IsFalse();

            var ctxFailing = new SmartPropEvaluationContext(new Dictionary<string, object?> { ["Count"] = 4 });
            await Assert.That(SmartPropModifierEvaluator.EvaluateElementModifiers(element, ctxFailing).IsFilteredOut).IsTrue();
        }

        [Test]
        public async Task SetTintColorEvaluatesAndNormalizesColor()
        {
            var tintOp = KVObject.Collection();
            tintOp["generic_data_type"] = new KVObject("CSmartPropOperation_SetTintColor");
            var choices = KVObject.Array();
            var choice = KVObject.Collection();
            var col = KVObject.Collection();
            col["m_SourceName"] = new KVObject("Tint_Color");
            choice["m_Color"] = col;
            choices.Add(choice);
            tintOp["m_ColorChoices"] = choices;
            var element = Element("Model", tintOp);
            float[] tintArray = [255f, 128f, 0f];
            var ctx = new SmartPropEvaluationContext(new Dictionary<string, object?>
            {
                ["Tint_Color"] = tintArray,
            });

            var result = SmartPropModifierEvaluator.EvaluateElementModifiers(element, ctx);
            await Assert.That(result.TintColor).IsNotNull();
            var tint = result.TintColor!.Value;
            await Assert.That(MathF.Abs(tint.X - 1.0f)).IsLessThan(Tolerance);
            await Assert.That(MathF.Abs(tint.Y - (128f / 255f))).IsLessThan(Tolerance);
            await Assert.That(MathF.Abs(tint.Z - 0f)).IsLessThan(Tolerance);
        }

        [Test]
        public async Task ReadVariableDefinitionsExtractsMetadata()
        {
            var root = KVObject.Collection();
            var vars = KVObject.Array();

            var varInt = KVObject.Collection();
            varInt["_class"] = new KVObject("CSmartPropVariable_Int");
            varInt["m_VariableName"] = new KVObject("Floodlight_Style");
            varInt["m_DefaultValue"] = new KVObject(1);
            varInt["m_bExposeAsParameter"] = new KVObject(true);
            varInt["m_nParamaterMinValue"] = new KVObject(1);
            varInt["m_nParamaterMaxValue"] = new KVObject(5);
            varInt["m_nElementID"] = new KVObject(10);
            vars.Add(varInt);

            var varMat = KVObject.Collection();
            varMat["_class"] = new KVObject("CSmartPropVariable_MaterialGroup");
            varMat["m_VariableName"] = new KVObject("Glow_Amount");
            varMat["m_sModelName"] = new KVObject("models/floodlight.vmdl");
            varMat["m_DefaultValue"] = new KVObject("on");
            varMat["m_nElementID"] = new KVObject(53);
            vars.Add(varMat);

            root["m_Variables"] = vars;

            var defs = SmartPropVariableMap.ReadVariableDefinitions(root);
            await Assert.That(defs.Count).IsEqualTo(2);

            var d0 = defs[0];
            await Assert.That(d0.Name).IsEqualTo("Floodlight_Style");
            await Assert.That(d0.Type).IsEqualTo("Int");
            await Assert.That(d0.DefaultValue).IsEqualTo(1);
            await Assert.That(d0.ExposeAsParameter).IsTrue();
            await Assert.That(d0.MinValue).IsEqualTo(1f);
            await Assert.That(d0.MaxValue).IsEqualTo(5f);
            await Assert.That(d0.ElementId).IsEqualTo(10);

            var d1 = defs[1];
            await Assert.That(d1.Name).IsEqualTo("Glow_Amount");
            await Assert.That(d1.Type).IsEqualTo("MaterialGroup");
            await Assert.That(d1.DefaultValue).IsEqualTo("on");
            await Assert.That(d1.ModelName).IsEqualTo("models/floodlight.vmdl");
            await Assert.That(d1.ElementId).IsEqualTo(53);
        }

        private static KVObject Binding(string key, KVObject value)
        {
            var binding = KVObject.Collection();
            binding[key] = value;
            return binding;
        }
    }
}
