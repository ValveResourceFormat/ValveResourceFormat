using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps.Criteria;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// CSmartPropElement_PickOne: evaluates one child. A stored choice is kept while it is still a candidate, a stored
    /// -1 evaluates none; otherwise RANDOM picks by weight from the master stream and FIRST takes the first candidate.
    /// </summary>
    internal sealed class PickOneElement(KVObject data, SmartPropParser parser) : SmartPropGroupElement(data, parser)
    {
        private readonly SmartPropAttribute selectionMode = Attr.Enum(data, "m_SelectionMode", 0);
        private readonly SmartPropAttribute specificChildIndex = Attr.Int(data, "m_SpecificChildIndex", 0);
        private readonly string outputChoiceVariableName = Attr.Plain(data, "m_OutputChoiceVariableName");

        public override void Evaluate(SmartPropContext ctx)
        {
            var mode = selectionMode.EvaluateEnum(ctx, SmartPropEnums.ChoiceSelectionMode);
            var chosen = -1;

            if (mode == 2)
            {
                if (Children.Count > 0)
                {
                    chosen = Math.Clamp(specificChildIndex.EvaluateInt(ctx), 0, Children.Count - 1);
                }
            }
            else
            {
                chosen = SelectCandidate(ctx, mode);
            }

            if (chosen >= 0)
            {
                ctx.EvaluateElement(Children[chosen]);
            }

            ctx.SetVariable(outputChoiceVariableName, SmartPropValue.FromInt(chosen));
        }

        private int SelectCandidate(SmartPropContext ctx, int mode)
        {
            var candidates = new List<(int Index, int ElementId, float Weight)>();

            for (var i = 0; i < Children.Count; i++)
            {
                var child = Children[i];

                if (!child.Enabled.EvaluateBool(ctx))
                {
                    continue;
                }

                var isValid = SmartPropCriteria.FindFirst<IsValidCriteria>(child.SelectionCriteria, ctx);

                if (isValid != null && !ctx.EvaluateExpressionBool(isValid.Expression.EvaluateString(ctx)))
                {
                    continue;
                }

                var weight = SmartPropCriteria.FindFirst<ChoiceWeightCriteria>(child.SelectionCriteria, ctx)?.Weight.EvaluateFloat(ctx) ?? 1f;
                candidates.Add((i, child.ElementId, weight));
            }

            if (candidates.Count == 0)
            {
                return -1;
            }

            var state = ctx.GetElementState();
            var previous = state.ChoiceValue == -1 ? -1 : candidates.FindIndex(c => c.ElementId == state.ChoiceValue);
            int pick;

            if (previous >= 0 || state.ChoiceValue == -1)
            {
                pick = previous;
            }
            else if (mode == 0)
            {
                var total = 0f;

                foreach (var candidate in candidates)
                {
                    total += candidate.Weight;
                }

                var roll = ctx.MasterStream.RandomFloat(0f, total);
                var sum = 0f;
                pick = 0;

                for (var i = 0; i < candidates.Count; i++)
                {
                    sum += candidates[i].Weight;

                    if (sum > roll)
                    {
                        pick = i;
                        break;
                    }
                }
            }
            else
            {
                pick = 0;
            }

            state.ChoiceValue = pick >= 0 ? candidates[pick].ElementId : -1;
            return pick >= 0 ? candidates[pick].Index : -1;
        }
    }
}
