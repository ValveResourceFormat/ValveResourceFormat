using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps.Criteria;

namespace ValveResourceFormat.ResourceTypes.SmartProps.Elements
{
    /// <summary>
    /// CSmartPropElement_PickOne: evaluates one child. A stored choice is kept while it is still a candidate, a stored
    /// -1 evaluates none; otherwise RANDOM picks by weight from the master stream and FIRST takes the first candidate.
    /// A matching choice request moves, clears or redraws the stored choice.
    /// </summary>
    internal sealed class PickOneElement(KVObject data, SmartPropParser parser) : SmartPropGroupElement(data, parser)
    {
        private const int NotFound = -2;

        private readonly SmartPropAttribute selectionMode = Attr.Enum(data, "m_SelectionMode", 0);
        private readonly SmartPropAttribute specificChildIndex = Attr.Int(data, "m_SpecificChildIndex", 0);
        private readonly string outputChoiceVariableName = Attr.Plain(data, "m_OutputChoiceVariableName");
        private readonly SmartPropAttribute configurable = Attr.Bool(data, "m_bConfigurable", true);
        private readonly SmartPropAttribute handleOffset = Attr.Vector3(data, "m_vHandleOffset", Vector3.Zero);
        private readonly SmartPropAttribute handleColor = Attr.Color(data, "m_HandleColor", new Color32(0x90, 0x90, 0x90, 0xFF));
        private readonly SmartPropAttribute handleSize = Attr.Int(data, "m_HandleSize", 9);
        private readonly int handleShape = SmartPropEnums.FromMember(data, "m_HandleShape", SmartPropEnums.ConfigurationHandleShape, 1);

        private readonly record struct Candidate(int Index, int ElementId, float Weight);

        public override void Evaluate(SmartPropContext ctx)
        {
            var mode = (ChoiceSelectionMode)selectionMode.EvaluateEnum(ctx, SmartPropEnums.ChoiceSelectionMode);
            var chosen = -1;

            if (mode == ChoiceSelectionMode.Specific)
            {
                chosen = Math.Min(Math.Max(specificChildIndex.EvaluateInt(ctx), 0), Children.Count - 1);
            }
            else
            {
                var candidates = CollectCandidates(ctx);
                var pick = SelectChoiceIndex(ctx, mode, candidates);

                if (configurable.EvaluateBool(ctx))
                {
                    AddChoiceHandle(ctx);
                }

                if (pick >= 0 && pick < candidates.Count)
                {
                    chosen = candidates[pick].Index;
                }
            }

            if (chosen >= 0 && chosen < Children.Count)
            {
                ctx.EvaluateElement(Children[chosen]);
            }

            ctx.SetVariable(outputChoiceVariableName, SmartPropValue.FromInt(chosen));
        }

        private List<Candidate> CollectCandidates(SmartPropContext ctx)
        {
            var candidates = new List<Candidate>();

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
                candidates.Add(new Candidate(i, child.ElementId, weight));
            }

            return candidates;
        }

        /// <summary>
        /// The candidate index to evaluate, -1 for none, or <see cref="NotFound"/> without candidates. Stores the
        /// chosen element id, or -1, in the element state.
        /// </summary>
        private static int SelectChoiceIndex(SmartPropContext ctx, ChoiceSelectionMode mode, List<Candidate> candidates)
        {
            if (candidates.Count == 0)
            {
                return NotFound;
            }

            var state = ctx.GetElementState();
            var action = FindChoiceAction(ctx);
            var previous = state.ChoiceValue == -1 ? -1 : candidates.FindIndex(c => c.ElementId == state.ChoiceValue);

            if (previous < -1)
            {
                previous = NotFound;
            }

            var pick = PickChoiceIndex(ctx, mode, candidates, previous, action);

            if (pick >= 0)
            {
                state.ChoiceValue = candidates[pick].ElementId;
            }
            else if (pick == -1)
            {
                state.ChoiceValue = -1;
            }

            return pick;
        }

        private static int PickChoiceIndex(SmartPropContext ctx, ChoiceSelectionMode mode, List<Candidate> candidates, int previous, SmartPropChoiceAction action)
        {
            var count = candidates.Count;

            if (mode == ChoiceSelectionMode.Random && (action == SmartPropChoiceAction.Randomize || previous == NotFound))
            {
                return PickByWeight(ctx, candidates);
            }

            if (previous == NotFound)
            {
                return 0;
            }

            return action switch
            {
                SmartPropChoiceAction.ChooseNone => -1,
                SmartPropChoiceAction.Next => (previous + 1) % count,
                SmartPropChoiceAction.Previous => (count + previous - 1) % count,
                _ => previous,
            };
        }

        private static int PickByWeight(SmartPropContext ctx, List<Candidate> candidates)
        {
            var total = 0f;

            foreach (var candidate in candidates)
            {
                total += candidate.Weight;
            }

            var roll = ctx.MasterStream.RandomFloat(0f, total);
            var sum = 0f;

            for (var i = 0; i < candidates.Count; i++)
            {
                sum += candidates[i].Weight;

                if (sum > roll)
                {
                    return i;
                }
            }

            return 0;
        }

        private static SmartPropChoiceAction FindChoiceAction(SmartPropContext ctx)
        {
            var requests = ctx.Input.ChoiceRequests;

            if (requests == null)
            {
                return SmartPropChoiceAction.None;
            }

            var path = ctx.CurrentElementPath;

            foreach (var request in requests)
            {
                if (request.ElementPath.Length == 0 || request.ElementPath.AsSpan().SequenceEqual(path))
                {
                    return request.Action;
                }
            }

            return SmartPropChoiceAction.None;
        }

        private void AddChoiceHandle(SmartPropContext ctx)
        {
            var offset = handleOffset.EvaluateVector3(ctx);

            ctx.Output.ChoiceHandles.Add(new SmartPropChoiceHandle
            {
                ElementPath = ctx.CurrentElementPath,
                Transform = ctx.Transform.Concat(new SmartPropTransform(offset, 1f, Quaternion.Identity)),
                Color = handleColor.EvaluateColor(ctx),
                Shape = (SmartPropHandleShape)handleShape,
                Size = handleSize.EvaluateInt(ctx),
            });
        }
    }
}
