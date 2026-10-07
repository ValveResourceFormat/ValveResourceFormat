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
        private readonly SmartPropAttribute expression = Attr.String(data, "m_Expression");

        public override bool Apply(SmartPropContext ctx) => !ctx.EvaluateExpressionBool(expression.EvaluateString(ctx));
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
        private readonly int comparison;

        public VariableValueFilter(KVObject data) : base(data)
        {
            var variableComparison = SmartPropParser.Get(data, "m_VariableComparison");
            name = variableComparison == null ? string.Empty : Attr.Plain(variableComparison, "m_Name");
            value = SmartPropValue.FromKV(variableComparison == null ? null : SmartPropParser.Get(variableComparison, "m_Value"));
            comparison = variableComparison == null ? 0 : SmartPropEnums.FromName(SmartPropEnums.VariableComparison, Attr.Plain(variableComparison, "m_Comparison"));
        }

        public override bool Apply(SmartPropContext ctx)
        {
            var current = ctx.GetVariable(name);

            var keep = comparison switch
            {
                0 => current.IsEqual(value),
                1 => !current.IsEqual(value),
                2 => value.IsGreater(current),
                3 => value.IsGreater(current) || current.IsEqual(value),
                4 => current.IsGreater(value),
                5 => current.IsGreater(value) || current.IsEqual(value),
                _ => false,
            };

            return !keep;
        }
    }

    /// <summary>
    /// CSmartPropFilter_SurfaceProperties and CSmartPropFilter_MaterialAttributes: allowed and disallowed lists
    /// checked against the properties of the traced surface material. Material properties are not resolved, so
    /// the element is rejected exactly when the allowed list is not empty, as without a surface.
    /// </summary>
    internal sealed class SurfaceListFilter(KVObject data, string allowedKey) : SmartPropModifier(data)
    {
        private readonly bool hasAllowed = (data.GetArray(allowedKey)?.Count ?? 0) > 0;

        public override bool Apply(SmartPropContext ctx) => hasAllowed;
    }
}
