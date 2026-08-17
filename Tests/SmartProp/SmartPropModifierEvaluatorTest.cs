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

    }
}
