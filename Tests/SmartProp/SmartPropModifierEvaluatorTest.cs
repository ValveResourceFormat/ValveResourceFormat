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

    }
}
