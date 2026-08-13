using System.Globalization;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Metadata definition of an authored SmartProp variable.
    /// </summary>
    public sealed record SmartPropVariableDefinition(
        string Name,
        string Type,
        object? DefaultValue,
        bool ExposeAsParameter,
        float? MinValue = null,
        float? MaxValue = null,
        string? ModelName = null,
        string? DisplayName = null,
        int ElementId = 0);

    /// <summary>
    /// Builds the variable name to typed default value map from a smart prop root's
    /// m_Variables array. Variable defaults may themselves be variable or expression
    /// bindings; those are resolved here so consumers see concrete values.
    /// </summary>
    public static class SmartPropVariableMap
    {
        private const string VariableClassPrefix = "CSmartPropVariable_";

        /// <summary>
        /// Reads variable metadata definitions from a CSmartPropRoot's m_Variables array.
        /// </summary>
        public static IReadOnlyList<SmartPropVariableDefinition> ReadVariableDefinitions(KVObject? root)
        {
            List<SmartPropVariableDefinition> list = [];
            if (root is null || !root.TryGetValue("m_Variables", out var variablesNode) || !variablesNode.IsArray)
            {
                return list;
            }

            var span = variablesNode.AsArraySpan();
            for (var i = 0; i < span.Length; i++)
            {
                var entry = span[i];
                if (entry.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                if (!entry.TryGetValue("m_VariableName", out var nameNode) || nameNode.ValueType != KVValueType.String)
                {
                    continue;
                }

                var name = (string)nameNode;
                if (name.Length == 0)
                {
                    continue;
                }

                var type = ClassNameOf(entry) ?? "String";
                entry.TryGetValue("m_DefaultValue", out var defaultNode);
                var defaultVal = CoerceFromKV(type, defaultNode);

                var expose = !entry.TryGetValue("m_bExposeAsParameter", out var exposeNode) || exposeNode.ValueType != KVValueType.Boolean || (bool)exposeNode;

                float? minVal = null;
                if (entry.TryGetValue("m_nParamaterMinValue", out var minN))
                {
                    minVal = (float)minN;
                }
                else if (entry.TryGetValue("m_flParamaterMinValue", out var minFl))
                {
                    minVal = (float)minFl;
                }
                else if (entry.TryGetValue("m_nMinValue", out var minN2))
                {
                    minVal = (float)minN2;
                }
                else if (entry.TryGetValue("m_flMinValue", out var minFl2))
                {
                    minVal = (float)minFl2;
                }

                float? maxVal = null;
                if (entry.TryGetValue("m_nParamaterMaxValue", out var maxN))
                {
                    maxVal = (float)maxN;
                }
                else if (entry.TryGetValue("m_flParamaterMaxValue", out var maxFl))
                {
                    maxVal = (float)maxFl;
                }
                else if (entry.TryGetValue("m_nMaxValue", out var maxN2))
                {
                    maxVal = (float)maxN2;
                }
                else if (entry.TryGetValue("m_flMaxValue", out var maxFl2))
                {
                    maxVal = (float)maxFl2;
                }

                var modelName = entry.TryGetValue("m_sModelName", out var modelNode) && modelNode.ValueType == KVValueType.String ? (string)modelNode : null;
                var displayName = entry.TryGetValue("m_DisplayName", out var dispNode) && dispNode.ValueType == KVValueType.String ? (string)dispNode : null;
                var elementId = entry.TryGetValue("m_nElementID", out var eidNode) && eidNode.ValueType == KVValueType.Int32 ? (int)eidNode : 0;

                list.Add(new SmartPropVariableDefinition(
                    name,
                    type,
                    defaultVal,
                    expose,
                    minVal,
                    maxVal,
                    modelName,
                    displayName,
                    elementId));
            }

            return list;
        }

        /// <summary>
        /// Reads m_Variables from a CSmartPropRoot and returns name to typed default,
        /// applying choices when present.
        /// Vector-like variables map to float arrays, numeric ones to int or float,
        /// bools to bool, and strings, enums and asset types to string.
        /// </summary>
        /// <param name="root">The root smart prop KVObject.</param>
        /// <param name="selectedChoices">Optional map of choice name to chosen option name.</param>
        public static Dictionary<string, object?> Build(KVObject? root, IReadOnlyDictionary<string, string>? selectedChoices = null)
        {
            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (root is null)
            {
                return map;
            }

            var choices = SmartPropChoiceMap.ReadChoices(root);

            if (!root.TryGetValue("m_Variables", out var variablesNode) || !variablesNode.IsArray)
            {
                if (choices.Count > 0)
                {
                    SmartPropChoiceMap.ApplyChoices(map, choices, selectedChoices);
                }

                return map;
            }

            Dictionary<string, (string? Class, KVObject Binding)> bound = new(StringComparer.OrdinalIgnoreCase);
            var span = variablesNode.AsArraySpan();
            for (var i = 0; i < span.Length; i++)
            {
                var entry = span[i];
                if (entry.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                if (!entry.TryGetValue("m_VariableName", out var nameNode) || nameNode.ValueType != KVValueType.String)
                {
                    continue;
                }

                var name = (string)nameNode;
                if (name.Length == 0)
                {
                    continue;
                }

                var className = ClassNameOf(entry);
                entry.TryGetValue("m_DefaultValue", out var defaultNode);

                if (IsBinding(defaultNode))
                {
                    // Seed with a typed zero so referencing variables resolve cleanly
                    // before their source has been evaluated.
                    bound[name] = (className, defaultNode!);
                    map[name] = ZeroValue(className);
                }
                else
                {
                    map[name] = CoerceFromKV(className, defaultNode);
                }
            }

            if (choices.Count > 0)
            {
                SmartPropChoiceMap.ApplyChoices(map, choices, selectedChoices);
            }

            if (bound.Count > 0)
            {
                ResolveBoundDefaults(map, bound);
            }

            return map;
        }

        private static string? ClassNameOf(KVObject entry)
        {
            if ((!entry.TryGetValue("generic_data_type", out var classNode) || classNode.ValueType != KVValueType.String)
                && (!entry.TryGetValue("_class", out classNode) || classNode.ValueType != KVValueType.String))
            {
                return null;
            }

            var className = (string)classNode;
            return className.StartsWith(VariableClassPrefix, StringComparison.Ordinal)
                ? className[VariableClassPrefix.Length..]
                : className;
        }

        private static bool IsBinding(KVObject? node)
            => node is { ValueType: KVValueType.Collection } collection
                && (collection.ContainsKey("m_SourceName") || collection.ContainsKey("m_Expression"));

        // Bindings may chain (A references B, whose default references C), so iterate
        // until nothing changes. The pass count bound also terminates reference cycles,
        // which settle on their seeded zero values.
        private static void ResolveBoundDefaults(Dictionary<string, object?> map, Dictionary<string, (string? Class, KVObject Binding)> bound)
        {
            for (var pass = 0; pass <= bound.Count; pass++)
            {
                var context = new SmartPropEvaluationContext(map);
                var changed = false;
                foreach (var (name, item) in bound)
                {
                    var resolved = ResolveBinding(item.Class, item.Binding, context);
                    if (!ValuesEqual(resolved, map.GetValueOrDefault(name)))
                    {
                        map[name] = resolved;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        private static object? ResolveBinding(string? className, KVObject binding, SmartPropEvaluationContext context)
        {
            var arity = VectorArity(className);
            if (arity > 0)
            {
                return arity switch
                {
                    2 => Floats2(context.ResolveVector3(binding)),
                    4 => [.. Floats4(context.ResolveVector4(binding))],
                    _ => [.. Floats3(context.ResolveVector3(binding))],
                };
            }

            // A direct variable reference adopts the referenced variable's already typed
            // value (keeping strings as strings); expressions and unresolved references
            // fall back to numeric evaluation.
            if (binding.TryGetValue("m_SourceName", out var source)
                && source.ValueType == KVValueType.String
                && !binding.ContainsKey("m_Expression"))
            {
                var referenced = context.GetVariable((string)source);
                if (referenced != null)
                {
                    return CoerceFromValue(className, referenced);
                }
            }

            return CoerceFromValue(className, context.ResolveScalar(binding, 0f));
        }

        private static float[] Floats2(Vector3 vector) => [vector.X, vector.Y];

        private static float[] Floats3(Vector3 vector) => [vector.X, vector.Y, vector.Z];

        private static float[] Floats4(Vector4 vector) => [vector.X, vector.Y, vector.Z, vector.W];

        internal static object? CoerceFromKV(string? className, KVObject? defaultValue)
        {
            var c = Lower(className);
            if (defaultValue is null || defaultValue.IsNull)
            {
                return ZeroValue(className);
            }

            if (c.Contains("bool"))
            {
                if (defaultValue.ValueType == KVValueType.String)
                {
                    var text = ((string)defaultValue).Trim().ToLowerInvariant();
                    return text is "1" or "true" or "yes";
                }

                if (defaultValue.ValueType == KVValueType.Boolean)
                {
                    return (bool)defaultValue;
                }

                return TryNumeric(defaultValue, out var number) && number != 0f;
            }

            if (c == "int")
            {
                return TryNumeric(defaultValue, out var number) ? (int)number : PrimitiveOf(defaultValue);
            }

            if (c == "float")
            {
                return TryNumeric(defaultValue, out var number) ? number : PrimitiveOf(defaultValue);
            }

            if (IsVectorClass(c))
            {
                return ToFloatList(defaultValue);
            }

            return PrimitiveOf(defaultValue);
        }

        private static object? CoerceFromValue(string? className, object? value)
        {
            var c = Lower(className);
            if (value is null)
            {
                return ZeroValue(className);
            }

            if (c.Contains("bool"))
            {
                return value switch
                {
                    bool b => b,
                    string s => s.Trim().ToLowerInvariant() is "1" or "true" or "yes",
                    _ => ToFloat(value) != 0f,
                };
            }

            if (c == "int")
            {
                return (int)ToFloat(value);
            }

            if (c == "float")
            {
                return ToFloat(value);
            }

            if (IsVectorClass(c))
            {
                return value switch
                {
                    float[] v => v,
                    string s => ParseFloatList(s),
                    bool b => [b ? 1f : 0f],
                    _ => [ToFloat(value)],
                };
            }

            return value;
        }

        private static object? ZeroValue(string? className)
        {
            var c = Lower(className);
            if (c.Contains("bool"))
            {
                return false;
            }

            if (c == "int")
            {
                return 0;
            }

            if (c == "float")
            {
                return 0f;
            }

            if (c.Contains("vector2d"))
            {
                return new float[2];
            }

            if (c.Contains("vector3d") || c == "angles")
            {
                return new float[3];
            }

            if (c.Contains("vector4d") || c.Contains("color"))
            {
                return new float[4];
            }

            return null;
        }

        private static int VectorArity(string? className)
        {
            var c = Lower(className);
            if (c.Contains("vector2d"))
            {
                return 2;
            }

            if (c.Contains("vector3d") || c == "angles")
            {
                return 3;
            }

            if (c.Contains("vector4d") || c.Contains("color"))
            {
                return 4;
            }

            return 0;
        }

        private static bool IsVectorClass(string lowerClassName)
            => lowerClassName.Contains("vector") || lowerClassName.Contains("color") || lowerClassName == "angles";

        private static object? PrimitiveOf(KVObject value) => value.ValueType switch
        {
            KVValueType.String => (string)value,
            KVValueType.Boolean => (bool)value,
            KVValueType.Int32 => (int)value,
            KVValueType.Int64 => (long)value,
            KVValueType.UInt32 => (uint)value,
            KVValueType.UInt64 => (ulong)value,
            KVValueType.Int16 => (short)value,
            KVValueType.UInt16 => (ushort)value,
            KVValueType.FloatingPoint => (float)value,
            KVValueType.FloatingPoint64 => (double)value,
            _ => null,
        };

        private static float[] ToFloatList(KVObject value)
        {
            if (value.IsArray)
            {
                var span = value.AsArraySpan();
                var result = new float[span.Length];
                for (var i = 0; i < span.Length; i++)
                {
                    result[i] = TryNumeric(span[i], out var number) ? number : 0f;
                }

                return result;
            }

            if (value.ValueType == KVValueType.Collection
                && value.TryGetValue("m_Components", out var components)
                && components.IsArray)
            {
                return ToFloatList(components);
            }

            if (value.ValueType == KVValueType.String)
            {
                return ParseFloatList((string)value);
            }

            return TryNumeric(value, out var single) ? [single] : [];
        }

        private static float[] ParseFloatList(string text)
        {
            List<float> values = [];
            foreach (var part in text.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    values.Add(parsed);
                }
            }

            return [.. values];
        }

        private static bool TryNumeric(KVObject value, out float result)
        {
            switch (value.ValueType)
            {
                case KVValueType.Int16:
                case KVValueType.Int32:
                case KVValueType.Int64:
                case KVValueType.UInt16:
                case KVValueType.UInt32:
                case KVValueType.UInt64:
                case KVValueType.FloatingPoint:
                case KVValueType.FloatingPoint64:
                case KVValueType.Boolean:
                    result = (float)value;
                    return true;
                case KVValueType.String:
                    return float.TryParse(((string)value).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
                default:
                    result = 0f;
                    return false;
            }
        }

        private static float ToFloat(object? value) => value switch
        {
            null => 0f,
            bool b => b ? 1f : 0f,
            int i => i,
            long l => l,
            float f => f,
            double d => (float)d,
            string s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f,
            _ => 0f,
        };

        private static bool ValuesEqual(object? a, object? b)
        {
            if (a is float[] left && b is float[] right)
            {
                return left.AsSpan().SequenceEqual(right);
            }

            return Equals(a, b);
        }

        private static string Lower(string? value) => value?.ToLowerInvariant() ?? string.Empty;
    }
}
