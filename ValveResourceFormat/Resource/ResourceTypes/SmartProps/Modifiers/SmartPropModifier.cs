using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Modifiers
{
    /// <summary>
    /// An operation or filter in an element's modifier list. Modifiers run in order before the element.
    /// </summary>
    internal abstract class SmartPropModifier(KVObject data)
    {
        public SmartPropAttribute Enabled { get; } = Attr.Bool(data, "m_bEnabled", true);

        /// <summary>Applies the modifier; true discards the element and skips the remaining modifiers.</summary>
        public abstract bool Apply(SmartPropContext ctx);

        protected static SmartPropSpace Space(SmartPropContext ctx, SmartPropAttribute attribute)
            => (SmartPropSpace)attribute.EvaluateEnum(ctx, SmartPropEnums.Space);
    }

    /// <summary>CSmartPropFilter_Probability: keeps the element with probability <c>m_flProbability</c>.</summary>
    internal sealed class ProbabilityFilter(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute probability = Attr.Float(data, "m_flProbability", 0.5f);

        public override bool Apply(SmartPropContext ctx)
        {
            var p = probability.EvaluateFloat(ctx);

            if (p >= 1f)
            {
                return false;
            }

            if (p <= 0f)
            {
                return true;
            }

            return p < ctx.GetRandomStream().RandomFloat(0f, 1f);
        }
    }

    /// <summary>CSmartPropFilter_Expression: keeps the element when the expression is true.</summary>
    internal sealed class ExpressionFilter(KVObject data) : SmartPropModifier(data)
    {
        private readonly string expression = Attr.Plain(data, "m_Expression");

        public override bool Apply(SmartPropContext ctx) => !ctx.EvaluateExpressionBool(expression);
    }

    /// <summary>CSmartPropFilter_SurfaceAngle: keeps the element when the surface slope is within the range.</summary>
    internal sealed class SurfaceAngleFilter(KVObject data) : SmartPropModifier(data)
    {
        private readonly SmartPropAttribute slopeMin = Attr.Float(data, "m_flSurfaceSlopeMin", 0f);
        private readonly SmartPropAttribute slopeMax = Attr.Float(data, "m_flSurfaceSlopeMax", 180f);

        public override bool Apply(SmartPropContext ctx)
        {
            var min = slopeMin.EvaluateFloat(ctx);
            var max = slopeMax.EvaluateFloat(ctx);

            if (min <= 0f && max >= 180f)
            {
                return false;
            }

            var z = Math.Clamp(ctx.State.SurfaceNormal.Z, -1f, 1f);
            return z < MathF.Cos(float.DegreesToRadians(max)) || z > MathF.Cos(float.DegreesToRadians(min));
        }
    }

    /// <summary>CSmartPropFilter_VariableValue: keeps the element when a variable compares true against a value.</summary>
    internal sealed class VariableValueFilter : SmartPropModifier
    {
        private readonly string name;
        private readonly SmartPropValue value;
        private readonly SmartPropVariableComparison comparison;

        public VariableValueFilter(KVObject data) : base(data)
        {
            var variableComparison = SmartPropParser.Get(data, "m_VariableComparison");
            name = variableComparison == null ? string.Empty : Attr.Plain(variableComparison, "m_Name");
            value = SmartPropValue.FromKV(variableComparison == null ? null : SmartPropParser.Get(variableComparison, "m_Value"));
            comparison = variableComparison == null
                ? SmartPropVariableComparison.Equal
                : (SmartPropVariableComparison)SmartPropEnums.FromName(SmartPropEnums.VariableComparison, Attr.Plain(variableComparison, "m_Comparison"));
        }

        public override bool Apply(SmartPropContext ctx)
        {
            var current = ctx.GetVariable(name);

            var keep = comparison switch
            {
                SmartPropVariableComparison.Equal => current.IsEqual(value),
                SmartPropVariableComparison.NotEqual => !current.IsEqual(value),
                SmartPropVariableComparison.Less => value.IsGreater(current),
                SmartPropVariableComparison.LessOrEqual => value.IsGreater(current) || current.IsEqual(value),
                SmartPropVariableComparison.Greater => current.IsGreater(value),
                SmartPropVariableComparison.GreaterOrEqual => current.IsGreater(value) || current.IsEqual(value),
                _ => false,
            };

            return !keep;
        }
    }

    /// <summary>
    /// CSmartPropFilter_SurfaceProperties: keeps the element when the surface property of the surface material is not
    /// disallowed and, when an allowed list is given, is in it. Names match case-insensitively; without a surface
    /// material or a surface property lookup the property is empty.
    /// </summary>
    internal sealed class SurfacePropertiesFilter(KVObject data) : SmartPropModifier(data)
    {
        private readonly string[] allowed = Attr.PlainStrings(data, "m_AllowedSurfaceProperties");
        private readonly string[] disallowed = Attr.PlainStrings(data, "m_DisallowedSurfaceProperties");

        public override bool Apply(SmartPropContext ctx)
        {
            if (allowed.Length == 0 && disallowed.Length == 0)
            {
                return false;
            }

            var property = ctx.GetSurfaceProperty();

            if (Contains(disallowed, property))
            {
                return true;
            }

            return allowed.Length > 0 && !Contains(allowed, property);
        }

        private static bool Contains(string[] names, string name) => Array.Exists(names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// CSmartPropFilter_MaterialAttributes: keeps the element when the surface material sets none of the disallowed
    /// integer attributes and, when an allowed list is given, sets one of the allowed ones. Without a surface material
    /// or a material attribute lookup no attribute is set.
    /// </summary>
    internal sealed class MaterialAttributesFilter(KVObject data) : SmartPropModifier(data)
    {
        private readonly string[] allowed = Attr.PlainStrings(data, "m_AllowedMaterialAttributes");
        private readonly string[] disallowed = Attr.PlainStrings(data, "m_DisallowedMaterialAttributes");

        public override bool Apply(SmartPropContext ctx)
        {
            if (allowed.Length == 0 && disallowed.Length == 0)
            {
                return false;
            }

            if (HasAny(ctx, disallowed))
            {
                return true;
            }

            return allowed.Length > 0 && !HasAny(ctx, allowed);
        }

        private static bool HasAny(SmartPropContext ctx, string[] attributes) => Array.Exists(attributes, attribute => ctx.GetMaterialAttribute(attribute) != 0);
    }
}
