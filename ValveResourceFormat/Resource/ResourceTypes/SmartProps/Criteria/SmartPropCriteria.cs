using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Criteria
{
    /// <summary>
    /// A selection criteria of a child element, read by the parent element that chooses between its children.
    /// </summary>
    internal abstract class SmartPropCriteria(KVObject data)
    {
        public SmartPropAttribute Enabled { get; } = Attr.Bool(data, "m_bEnabled", true);

        /// <summary>The first criteria of the given class whose <c>m_bEnabled</c> is true.</summary>
        public static T? FindFirst<T>(List<SmartPropCriteria> criteria, SmartPropContext ctx) where T : SmartPropCriteria
        {
            foreach (var item in criteria)
            {
                if (item is T typed && item.Enabled.EvaluateBool(ctx))
                {
                    return typed;
                }
            }

            return null;
        }
    }

    internal sealed class IsValidCriteria(KVObject data) : SmartPropCriteria(data)
    {
        public SmartPropAttribute Expression { get; } = Attr.String(data, "m_Expression");
    }

    internal sealed class ChoiceWeightCriteria(KVObject data) : SmartPropCriteria(data)
    {
        public SmartPropAttribute Weight { get; } = Attr.Float(data, "m_flWeight", 1f);
    }

    internal sealed class EndCapCriteria(KVObject data) : SmartPropCriteria(data)
    {
        public SmartPropAttribute Start { get; } = Attr.Bool(data, "m_bStart", true);
        public SmartPropAttribute End { get; } = Attr.Bool(data, "m_bEnd", true);
    }

    internal sealed class LinearLengthCriteria(KVObject data) : SmartPropCriteria(data)
    {
        public SmartPropAttribute Length { get; } = Attr.Float(data, "m_flLength", 1f);
        public SmartPropAttribute AllowScale { get; } = Attr.Bool(data, "m_bAllowScale", false);
        public SmartPropAttribute MinLength { get; } = Attr.Float(data, "m_flMinLength", 1f);
        public SmartPropAttribute MaxLength { get; } = Attr.Float(data, "m_flMaxLength", 1f);
    }

    internal sealed class PathPositionCriteria(KVObject data) : SmartPropCriteria(data)
    {
        public SmartPropAttribute PlaceAtPositions { get; } = Attr.Enum(data, "m_PlaceAtPositions", 0);
        public SmartPropAttribute PlaceEveryNthPosition { get; } = Attr.Int(data, "m_nPlaceEveryNthPosition", 2);
        public SmartPropAttribute NthPositionIndexOffset { get; } = Attr.Int(data, "m_nNthPositionIndexOffset", 0);
        public SmartPropAttribute AllowAtStart { get; } = Attr.Bool(data, "m_bAllowAtStart", true);
        public SmartPropAttribute AllowAtEnd { get; } = Attr.Bool(data, "m_bAllowAtEnd", true);
    }
}
