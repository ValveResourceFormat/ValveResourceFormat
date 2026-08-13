using System.Globalization;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// Carries the state needed to resolve SmartProp field values: variable defaults,
    /// live overrides, per-instance placement info and the deterministic RNG shared by
    /// Random* expression functions.
    /// Field values come in several KV3 shapes (literals, numeric strings, variable
    /// bindings, expression bindings, per-component vectors); this class is the single
    /// place that turns every form into a concrete value.
    /// </summary>
    public sealed class SmartPropEvaluationContext
    {
        private readonly Dictionary<string, object?> variables;
        private readonly Dictionary<string, object?> overrides;

        /// <summary>Zero based index of the instance being evaluated.</summary>
        public int InstanceIndex { get; }

        /// <summary>Total number of instances the owning element produces.</summary>
        public int InstanceCount { get; }

        /// <summary>Scale factor applied by the enclosing FitOnLine style placement.</summary>
        public float LinearScale { get; }

        /// <summary>Deterministic RNG shared across derived per-instance contexts.</summary>
        public Random Rng { get; private set; }

        /// <summary>
        /// Creates an evaluation context. Variable names compare case-insensitively,
        /// matching how Source 2 resolves them.
        /// </summary>
        public SmartPropEvaluationContext(
            IReadOnlyDictionary<string, object?>? variables = null,
            int instanceIndex = 0,
            int instanceCount = 1,
            int seed = 0,
            IReadOnlyDictionary<string, object?>? overrides = null,
            float linearScale = 1f)
        {
            this.variables = CopyMap(variables);
            this.overrides = CopyMap(overrides);
            InstanceIndex = instanceIndex;
            InstanceCount = instanceCount;
            LinearScale = linearScale;
            Rng = new Random(seed);
        }

        /// <summary>
        /// Returns a copy of this context with updated instance placement info.
        /// The copy shares the parent's RNG so random sequences continue seamlessly.
        /// </summary>
        public SmartPropEvaluationContext WithInstance(int? instanceIndex = null, int? instanceCount = null, float? linearScale = null)
            => new(variables, instanceIndex ?? InstanceIndex, instanceCount ?? InstanceCount, 0, overrides, linearScale ?? LinearScale)
            {
                Rng = Rng,
            };

        /// <summary>
        /// Looks up a variable by name. Overrides win over declared defaults.
        /// Returns null when no variable matches.
        /// </summary>
        public object? GetVariable(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            if (overrides.TryGetValue(name, out var value))
            {
                return value;
            }

            return variables.TryGetValue(name, out var fallback) ? fallback : null;
        }

        /// <summary>Sets a live variable override that wins over declared defaults.</summary>
        public void SetOverride(string name, object? value) => overrides[name] = value;

        /// <summary>
        /// Resolves any scalar value form to a float. Bindings are dictionaries with
        /// m_Expression, m_SourceName or m_Components; arrays resolve to their first
        /// component.
        /// </summary>
        public float ResolveScalar(KVObject? value, float defaultResult = 0f)
        {
            if (value is null || value.IsNull)
            {
                return defaultResult;
            }

            switch (value.ValueType)
            {
                case KVValueType.Boolean:
                    return (bool)value ? 1f : 0f;
                case KVValueType.String:
                {
                    var text = ((string)value).Trim();
                    if (text.Length == 0)
                    {
                        return defaultResult;
                    }

                    return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : SmartPropExpressionEvaluator.Evaluate(text, this, defaultResult);
                }

                case KVValueType.Collection:
                    if (value.TryGetValue("m_Expression", out var expression) && expression.ValueType == KVValueType.String)
                    {
                        return SmartPropExpressionEvaluator.Evaluate((string)expression, this, defaultResult);
                    }

                    if (value.TryGetValue("m_SourceName", out var source) && source.ValueType == KVValueType.String)
                    {
                        return ResolveVariableScalar((string)source, defaultResult);
                    }

                    if (value.TryGetValue("m_Components", out var components) && components.IsArray)
                    {
                        var componentsSpan = components.AsArraySpan();
                        return componentsSpan.Length > 0 ? ResolveScalar(componentsSpan[0], defaultResult) : defaultResult;
                    }

                    return defaultResult;
                case KVValueType.Array:
                {
                    var span = value.AsArraySpan();
                    return span.Length > 0 ? ResolveScalar(span[0], defaultResult) : defaultResult;
                }

                default:
                    return IsNumeric(value.ValueType) ? (float)value : defaultResult;
            }
        }

        /// <summary>
        /// Resolves any string value form. String fields (e.g. m_sModelName) can be a
        /// literal, a variable binding, or an expression whose text is the literal value.
        /// </summary>
        public string ResolveString(KVObject? value, string? defaultResult = null)
        {
            var fallback = defaultResult ?? string.Empty;
            if (value is null || value.IsNull)
            {
                return fallback;
            }

            switch (value.ValueType)
            {
                case KVValueType.String:
                    return (string)value;
                case KVValueType.Boolean:
                    return (bool)value ? "true" : "false";
                case KVValueType.Collection:
                    if (value.TryGetValue("m_SourceName", out var source) && source.ValueType == KVValueType.String)
                    {
                        return ResolveVariableString((string)source, fallback);
                    }

                    if (value.TryGetValue("m_Expression", out var expression) && expression.ValueType == KVValueType.String)
                    {
                        // String fields keep the literal value in the expression text;
                        // the numeric evaluator cannot produce strings, so use it verbatim.
                        var text = (string)expression;
                        return text.Length > 0 ? text : fallback;
                    }

                    return fallback;
                default:
                    return IsNumeric(value.ValueType) ? ((float)value).ToString(CultureInfo.InvariantCulture) : fallback;
            }
        }

        /// <summary>
        /// Resolves any vector value form to a Vector3. Scalar forms broadcast to all
        /// three axes; arrays and m_Components resolve per component.
        /// </summary>
        public Vector3 ResolveVector3(KVObject? value, Vector3 defaultResult = default)
        {
            if (value is null || value.IsNull)
            {
                return defaultResult;
            }

            switch (value.ValueType)
            {
                case KVValueType.Array:
                {
                    var span = value.AsArraySpan();
                    return new Vector3(
                        Component(span, 0, defaultResult.X),
                        Component(span, 1, defaultResult.Y),
                        Component(span, 2, defaultResult.Z));
                }

                case KVValueType.Collection:
                    if (value.TryGetValue("m_Components", out var components) && components.IsArray)
                    {
                        return ResolveVector3(components, defaultResult);
                    }

                    if (value.TryGetValue("m_SourceName", out var source) && source.ValueType == KVValueType.String)
                    {
                        return ResolveVariableVector3((string)source, defaultResult);
                    }

                    if (value.TryGetValue("m_Expression", out var expression) && expression.ValueType == KVValueType.String)
                    {
                        var scalar = SmartPropExpressionEvaluator.Evaluate((string)expression, this, defaultResult.X);
                        return new Vector3(scalar, scalar, scalar);
                    }

                    return defaultResult;
                case KVValueType.Boolean:
                {
                    var scalar = (bool)value ? 1f : 0f;
                    return new Vector3(scalar, scalar, scalar);
                }

                default:
                {
                    if (!IsNumeric(value.ValueType))
                    {
                        return defaultResult;
                    }

                    var scalar = (float)value;
                    return new Vector3(scalar, scalar, scalar);
                }
            }
        }

        /// <summary>
        /// Resolves any four component value form (Vector4D, colors) to a Vector4.
        /// </summary>
        public Vector4 ResolveVector4(KVObject? value, Vector4 defaultResult = default)
        {
            if (value is null || value.IsNull)
            {
                return defaultResult;
            }

            switch (value.ValueType)
            {
                case KVValueType.Array:
                {
                    var span = value.AsArraySpan();
                    return new Vector4(
                        Component(span, 0, defaultResult.X),
                        Component(span, 1, defaultResult.Y),
                        Component(span, 2, defaultResult.Z),
                        Component(span, 3, defaultResult.W));
                }

                case KVValueType.Collection:
                    if (value.TryGetValue("m_Components", out var components) && components.IsArray)
                    {
                        return ResolveVector4(components, defaultResult);
                    }

                    if (value.TryGetValue("m_SourceName", out var source) && source.ValueType == KVValueType.String)
                    {
                        return ResolveVariableVector4((string)source, defaultResult);
                    }

                    if (value.TryGetValue("m_Expression", out var expression) && expression.ValueType == KVValueType.String)
                    {
                        var scalar = SmartPropExpressionEvaluator.Evaluate((string)expression, this, defaultResult.X);
                        return new Vector4(scalar, scalar, scalar, scalar);
                    }

                    return defaultResult;
                case KVValueType.Boolean:
                {
                    var scalar = (bool)value ? 1f : 0f;
                    return new Vector4(scalar, scalar, scalar, scalar);
                }

                default:
                {
                    if (!IsNumeric(value.ValueType))
                    {
                        return defaultResult;
                    }

                    var scalar = (float)value;
                    return new Vector4(scalar, scalar, scalar, scalar);
                }
            }
        }

        /// <summary>Resolves an angle value form to (pitch, yaw, roll) in degrees.</summary>
        public Vector3 ResolveAngles(KVObject? value, Vector3 defaultResult = default) => ResolveVector3(value, defaultResult);

        private float ResolveVariableScalar(string name, float defaultResult)
        {
            return GetVariable(name) switch
            {
                null => defaultResult,
                bool b => b ? 1f : 0f,
                int i => i,
                long l => l,
                float f => f,
                double d => (float)d,
                float[] { Length: > 0 } v => v[0],
                string s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : SmartPropExpressionEvaluator.Evaluate(s, this, defaultResult),
                _ => defaultResult,
            };
        }

        private string ResolveVariableString(string name, string fallback)
        {
            return GetVariable(name) switch
            {
                null => fallback,
                bool b => b ? "true" : "false",
                string s => s,
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                float f => f.ToString(CultureInfo.InvariantCulture),
                double d => d.ToString(CultureInfo.InvariantCulture),
                _ => fallback,
            };
        }

        private Vector3 ResolveVariableVector3(string name, Vector3 defaultResult)
        {
            return GetVariable(name) switch
            {
                float[] v => new Vector3(At(v, 0, defaultResult.X), At(v, 1, defaultResult.Y), At(v, 2, defaultResult.Z)),
                bool b => Vector3.One * (b ? 1f : 0f),
                int i => Vector3.One * i,
                long l => Vector3.One * l,
                float f => Vector3.One * f,
                double d => Vector3.One * (float)d,
                _ => defaultResult,
            };
        }

        private Vector4 ResolveVariableVector4(string name, Vector4 defaultResult)
        {
            return GetVariable(name) switch
            {
                float[] v => new Vector4(At(v, 0, defaultResult.X), At(v, 1, defaultResult.Y), At(v, 2, defaultResult.Z), At(v, 3, defaultResult.W)),
                bool b => Vector4.One * (b ? 1f : 0f),
                int i => Vector4.One * i,
                long l => Vector4.One * l,
                float f => Vector4.One * f,
                double d => Vector4.One * (float)d,
                _ => defaultResult,
            };
        }

        private float Component(ReadOnlySpan<KVObject> span, int index, float defaultResult)
            => index < span.Length ? ResolveScalar(span[index], defaultResult) : defaultResult;

        private static float At(float[] values, int index, float defaultResult)
            => index < values.Length ? values[index] : defaultResult;

        private static Dictionary<string, object?> CopyMap(IReadOnlyDictionary<string, object?>? source)
        {
            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (source != null)
            {
                foreach (var (key, value) in source)
                {
                    map[key] = value;
                }
            }

            return map;
        }

        private static bool IsNumeric(KVValueType type)
            => type is KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64
                or KVValueType.UInt16 or KVValueType.UInt32 or KVValueType.UInt64
                or KVValueType.FloatingPoint or KVValueType.FloatingPoint64;
    }
}
