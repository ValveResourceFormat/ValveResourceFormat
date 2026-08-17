using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropSelectionCriteriaTest
    {
        private static SmartPropEvaluationContext Context() => new();

        private static KVObject Child(params KVObject[] criteria)
        {
            var child = KVObject.Collection();
            var list = KVObject.Array();
            foreach (var criteriaNode in criteria)
            {
                list.Add(criteriaNode);
            }

            child["m_SelectionCriteria"] = list;
            return child;
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

        private static KVObject Str(string value) => new(value);

        private static KVObject Bool(bool value) => new(value);

        private static KVObject Float(float value) => new(value);

    }
}
