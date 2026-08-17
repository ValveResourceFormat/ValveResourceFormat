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

    }
}
