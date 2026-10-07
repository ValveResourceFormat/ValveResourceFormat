using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// The <c>smartprop1</c> to <c>smartprop2</c> format upgrade: element ids for nodes without one, and the typed
    /// <c>CSmartPropOperation_SetVariable*</c> operations read as <c>CSmartPropOperation_SetVariable</c>.
    /// </summary>
    internal static class SmartPropLegacyUpgrade
    {
        /// <summary>
        /// Gives every element and variable without a positive element id a fresh one, counting up from the larger
        /// of <c>_editor.next_element_id</c> and the largest id present.
        /// </summary>
        public static Dictionary<KVObject, int> AssignElementIds(KVObject root)
        {
            var assigned = new Dictionary<KVObject, int>(ReferenceEqualityComparer.Instance);
            var nodes = new List<KVObject>();
            CollectElements(root, nodes);
            nodes.AddRange(root.GetArray("m_Variables") ?? []);

            var largest = 0L;

            foreach (var node in nodes)
            {
                largest = Math.Max(largest, node.GetIntegerProperty("m_nElementID"));
            }

            var editor = SmartPropParser.Get(root, "_editor");
            var next = Math.Max(editor?.GetIntegerProperty("next_element_id", 1) ?? 1, largest + 1);

            foreach (var node in nodes)
            {
                if (node.GetIntegerProperty("m_nElementID") <= 0)
                {
                    assigned[node] = (int)next++;
                }
            }

            return assigned;
        }

        private static void CollectElements(KVObject node, List<KVObject> nodes)
        {
            foreach (var child in node.GetArray("m_Children") ?? [])
            {
                nodes.Add(child);
                CollectElements(child, nodes);
            }
        }

        /// <summary>
        /// Returns the variable data type a legacy typed SetVariable class writes, or null for any other class.
        /// </summary>
        public static string? GetLegacySetVariableType(string className)
        {
            const string Prefix = "CSmartPropOperation_SetVariable";

            if (!className.StartsWith(Prefix, StringComparison.Ordinal) || className.Length == Prefix.Length)
            {
                return null;
            }

            return className[Prefix.Length..] switch
            {
                "String" or "Model" => "STRING",
                "Bool" => "BOOL",
                "Int" => "INTEGER",
                "Float" => "FLOAT",
                "Vector2D" => "VECTOR2",
                "Vector3D" => "VECTOR3",
                "Vector4D" => "VECTOR4",
                "Color" => "COLOR",
                "Angles" => "ANGLES",
                var suffix when suffix.StartsWith("Enum_", StringComparison.Ordinal) => "STRING",
                _ => null,
            };
        }
    }
}
