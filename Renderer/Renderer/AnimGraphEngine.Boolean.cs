using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class BoolValueNode
    {
        bool cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public bool GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual bool GetValueInternal(GraphContext ctx)
        {
            ctx.LogNodeNotImplemented(NodeIdx, GetType().Name);
            return false;
        }
    }

    partial class ConstBoolNode
    {
        public override void Initialize(GraphContext ctx)
        {
            // No initialization needed for ConstBoolNode
        }

        protected override bool GetValueInternal(GraphContext ctx) => Value;
    }

    [DebuggerDisplay("{parameterName}")]
    partial class ControlParameterBoolNode
    {
        string parameterName;

        public override void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Graph.ParameterNames.Length);
            parameterName = ctx.Graph.ParameterNames[NodeIdx];
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            return ctx.Graph.BoolParameters[parameterName];
        }
    }

    partial class CachedBoolNode
    {
        BoolValueNode InputValueNode;
        bool CachedValue;
        bool HasCachedValue;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            if (!HasCachedValue)
            {
                Debug.Assert(Mode == CachedValueMode.OnExit);

                if (ctx.BranchState == BranchState.Inactive)
                {
                    HasCachedValue = true;
                }
                else
                {
                    CachedValue = InputValueNode.GetValue(ctx);
                }
            }

            return CachedValue;
        }
    }

    partial class AndNode
    {
        BoolValueNode[] conditionNodes;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref conditionNodes);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            foreach (var node in conditionNodes)
            {
                if (!node.GetValue(ctx))
                {
                    return false;
                }
            }

            return true;
        }
    }

    partial class FloatComparisonNode
    {
        FloatValueNode InputNode;
        FloatValueNode? ComparandNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputNode);
            ctx.SetOptionalNodeFromIndex(ComparandValueNodeIdx, ref ComparandNode);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            var a = InputNode.GetValue(ctx);
            var b = ComparandNode?.GetValue(ctx) ?? ComparisonValue;

            return Comparison switch
            {
                FloatComparisonNode__Comparison.LessThan => a < b,
                FloatComparisonNode__Comparison.LessThanEqual => a <= b,
                FloatComparisonNode__Comparison.GreaterThan => a > b,
                FloatComparisonNode__Comparison.GreaterThanEqual => a >= b,
                FloatComparisonNode__Comparison.NearEqual => MathF.Abs(a - b) <= Epsilon,
                _ => false,
            };
        }
    }

    partial class FloatRangeComparisonNode
    {
        FloatValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            var inputValue = InputValueNode.GetValue(ctx);
            var inRangeInclusive = inputValue >= Range.Min && inputValue <= Range.Max;
            var inRangeExclusive = inputValue > Range.Min && inputValue < Range.Max;

            return IsInclusiveCheck ? inRangeInclusive : inRangeExclusive;
        }
    }

    // Checks the sampled foot events for a phase (or phase group) match.
    partial class FootEventConditionNode
    {
        StateNode? SourceStateNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            var result = false;
            var eventFound = false;

            var ignoreInactiveEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.IgnoreInactiveEvents);
            var searchRange = EventSearch.CalculateSearchRange(ctx, SourceStateNode, EventConditionRules);

            for (var i = searchRange.StartIdx; i < searchRange.EndIdx && i < ctx.SampledEvents.Count; i++)
            {
                var sampledEvent = ctx.SampledEvents[i];
                if (sampledEvent.IsIgnored || sampledEvent.IsGraphEvent)
                {
                    continue;
                }

                if (ignoreInactiveEvents && !sampledEvent.IsFromActiveBranch)
                {
                    continue;
                }

                if (EventSearch.TryGetFootPhase(sampledEvent, out var foot))
                {
                    result = PhaseCondition switch
                    {
                        FootPhaseCondition.LeftFootDown => foot == FootPhase.LeftFootDown,
                        FootPhaseCondition.RightFootDown => foot == FootPhase.RightFootDown,
                        FootPhaseCondition.LeftFootPassing => foot == FootPhase.LeftFootPassing,
                        FootPhaseCondition.RightFootPassing => foot == FootPhase.RightFootPassing,
                        FootPhaseCondition.LeftPhase => foot is FootPhase.RightFootDown or FootPhase.LeftFootPassing,
                        FootPhaseCondition.RightPhase => foot is FootPhase.LeftFootDown or FootPhase.RightFootPassing,
                        FootPhaseCondition.None => foot == FootPhase.None,
                        _ => false,
                    };
                    eventFound = true;
                }

                if (eventFound)
                {
                    break;
                }
            }

            return result;
        }
    }

    // Checks the sampled graph events (entry/fully-in-state/exit/timed/generic) against a set of
    // ID + event-type conditions (Esoterica GraphEventConditionNode).
    partial class GraphEventConditionNode
    {
        StateNode? SourceStateNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override bool GetValueInternal(GraphContext ctx) => TryMatchTags(ctx);

        static bool DoesGraphEventTypesMatchCondition(GraphEventTypeCondition condition, GraphEventType eventType)
            => condition == GraphEventTypeCondition.Any || (byte)condition == (byte)eventType;

        private bool TryMatchTags(GraphContext ctx)
        {
            Span<bool> foundConditions = stackalloc bool[Conditions.Length];

            var searchRange = EventSearch.CalculateSearchRange(ctx, SourceStateNode, EventConditionRules);
            var ignoreInactiveEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.IgnoreInactiveEvents);
            var operatorOr = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.OperatorOr);

            for (var i = searchRange.StartIdx; i < searchRange.EndIdx && i < ctx.SampledEvents.Count; i++)
            {
                var sampledEvent = ctx.SampledEvents[i];

                if (sampledEvent.IsIgnored)
                {
                    continue;
                }

                if (ignoreInactiveEvents && !sampledEvent.IsFromActiveBranch)
                {
                    continue;
                }

                // Only check graph events
                if (sampledEvent.IsAnimationEvent)
                {
                    continue;
                }

                var foundID = sampledEvent.ID;
                if (!foundID.IsValid)
                {
                    continue;
                }

                // Check against the set of events we need to match
                for (var t = 0; t < Conditions.Length; t++)
                {
                    var condition = Conditions[t];
                    if (condition.EventID == foundID && DoesGraphEventTypesMatchCondition(condition.EventTypeCondition, sampledEvent.GraphEventType))
                    {
                        // If we have an 'or' operator we can early out here
                        if (operatorOr)
                        {
                            return true;
                        }

                        foundConditions[t] = true;
                        break;
                    }
                }
            }

            // Ensure that all conditions have been matched
            for (var t = 0; t < Conditions.Length; t++)
            {
                if (!foundConditions[t])
                {
                    return false;
                }
            }

            return true;
        }
    }

    // Compares the source state's current sync event index against a fixed index.
    partial class SyncEventIndexConditionNode
    {
        StateNode SourceStateNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            var syncTrack = SourceStateNode.SyncTrack;
            var currentSyncTime = syncTrack.GetTime(SourceStateNode.CurrentTime);

            return TriggerMode switch
            {
                SyncEventIndexConditionNode__TriggerMode.ExactlyAtEventIndex => currentSyncTime.EventIdx == SyncEventIdx,
                SyncEventIndexConditionNode__TriggerMode.GreaterThanEqualToEventIndex => currentSyncTime.EventIdx >= SyncEventIdx,
                _ => false,
            };
        }
    }

    // Checks the sampled events buffer for the given IDs, coming either from graph events or from
    // ID animation events on clip timelines.
    partial class IDEventConditionNode
    {
        StateNode? SourceStateNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override bool GetValueInternal(GraphContext ctx) => TryMatchTags(ctx);

        private SampledEventRange CalculateSearchRange(GraphContext ctx)
        {
            var restrictSearch = SourceStateNode != null && EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.LimitSearchToSourceState);

            return restrictSearch
                ? SourceStateNode!.SampledEventRange
                : new SampledEventRange(0, ctx.SampledEvents.Count);
        }

        private bool TryMatchTags(GraphContext ctx)
        {
            Span<bool> foundIDs = stackalloc bool[EventIDs.Length];

            var searchRange = CalculateSearchRange(ctx);
            var ignoreInactiveEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.IgnoreInactiveEvents);
            var searchAnimEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.SearchBothGraphAndAnimEvents)
                || EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.SearchOnlyAnimEvents);
            var searchGraphEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.SearchBothGraphAndAnimEvents)
                || EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.SearchOnlyGraphEvents);
            var operatorOr = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.OperatorOr);

            for (var i = searchRange.StartIdx; i < searchRange.EndIdx && i < ctx.SampledEvents.Count; i++)
            {
                var sampledEvent = ctx.SampledEvents[i];

                if (sampledEvent.IsIgnored)
                {
                    continue;
                }

                if (ignoreInactiveEvents && !sampledEvent.IsFromActiveBranch)
                {
                    continue;
                }

                GlobalSymbol foundID = default;

                if (sampledEvent.IsAnimationEvent)
                {
                    if (searchAnimEvents)
                    {
                        foundID = sampledEvent.ID;
                    }
                }
                else if (searchGraphEvents)
                {
                    foundID = sampledEvent.ID;
                }

                if (foundID.IsValid)
                {
                    for (var t = 0; t < EventIDs.Length; t++)
                    {
                        if (EventIDs[t] == foundID)
                        {
                            if (operatorOr)
                            {
                                return true;
                            }

                            foundIDs[t] = true;
                            break;
                        }
                    }
                }
            }

            // With the 'and' operator every requested ID must have been found
            for (var t = 0; t < EventIDs.Length; t++)
            {
                if (!foundIDs[t])
                {
                    return false;
                }
            }

            return true;
        }
    }

    [DebuggerDisplay("{Comparision} {ComparisionIDs}")]
    partial class IDComparisonNode
    {
        IDValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            var inputValue = InputValueNode.GetValue(ctx);

            // An empty comparand list matches an unset input (Esoterica IDComparisonNode)
            var matches = ComparisionIDs.Length == 0 ? !inputValue.IsValid : ComparisionIDs.Contains(inputValue);
            return Comparison switch
            {
                IDComparisonNode__Comparison.Matches => matches,
                IDComparisonNode__Comparison.DoesntMatch => !matches,
                _ => false,
            };
        }
    }

    // External poses and external graph slots are never attached in the viewer, so these are
    // faithfully always false.
    partial class IsExternalPoseSetNode
    {
        protected override bool GetValueInternal(GraphContext ctx) => false;
    }

    partial class IsExternalGraphSlotFilledNode
    {
        protected override bool GetValueInternal(GraphContext ctx) => false;
    }

    // CS2 addition with no Esoterica analogue: true while evaluating an inactive branch (e.g. the
    // source side of a transition).
    partial class IsInactiveBranchConditionNode
    {
        protected override bool GetValueInternal(GraphContext ctx) => ctx.BranchState == BranchState.Inactive;
    }

    partial class NotNode
    {
        BoolValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            return !InputValueNode.GetValue(ctx);
        }
    }

    partial class OrNode
    {
        BoolValueNode[] conditionNodes;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref conditionNodes);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            foreach (var node in conditionNodes)
            {
                if (node.GetValue(ctx))
                {
                    return true;
                }
            }

            return false;
        }
    }

    partial class StateCompletedConditionNode
    {
        StateNode SourceStateNode;
        FloatValueNode? TransitionDurationOverrideNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
            ctx.SetOptionalNodeFromIndex(TransitionDurationOverrideNodeIdx, ref TransitionDurationOverrideNode);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            var sourceDuration = SourceStateNode.Duration;

            if (sourceDuration == 0f)
            {
                return true;
            }

            var transitionDuration = TransitionDurationOverrideNode?.GetValue(ctx) ?? TransitionDurationSeconds;

            // Instant transitions complete at the end of the state, detected via the loop wrap
            if (transitionDuration <= 1e-5f)
            {
                return MathF.Abs(SourceStateNode.CurrentTime - 1f) <= 1e-5f
                    || SourceStateNode.CurrentTime < SourceStateNode.PreviousTime;
            }

            var transitionPoint = 1.0f - (transitionDuration / sourceDuration);
            return SourceStateNode.CurrentTime >= transitionPoint;
        }
    }

    // SyncEventIndexConditionNode

    partial class TimeConditionNode
    {
        StateNode SourceStateNode;
        FloatValueNode? InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
            ctx.SetOptionalNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        bool Compare(float a, float b)
        {
            return Operator switch
            {
                TimeConditionNode__Operator.LessThan => a < b,
                TimeConditionNode__Operator.LessThanEqual => a <= b,
                TimeConditionNode__Operator.GreaterThan => a > b,
                TimeConditionNode__Operator.GreaterThanEqual => a >= b,
                _ => false,
            };
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            var comparisonValue = InputValueNode?.GetValue(ctx) ?? Comparand;

            return Type switch
            {
                TimeConditionNode__ComparisonType.PercentageThroughState => Compare(SourceStateNode.CurrentTime, comparisonValue),
                TimeConditionNode__ComparisonType.PercentageThroughSyncEvent => Compare(SourceStateNode.SyncTrack.GetTime(SourceStateNode.CurrentTime).PercentageThrough.Value, comparisonValue),
                TimeConditionNode__ComparisonType.ElapsedTime => Compare(SourceStateNode.Duration * SourceStateNode.CurrentTime, comparisonValue),
                _ => false,
            };
        }
    }

    partial class TransitionEventConditionNode
    {
        StateNode? SourceStateNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override bool GetValueInternal(GraphContext ctx)
        {
            var eventFound = false;
            var mostRestrictiveMarkerFound = TransitionRule.AllowTransition;

            var ignoreInactiveEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.IgnoreInactiveEvents);

            var searchRange = EventSearch.CalculateSearchRange(ctx, SourceStateNode, EventConditionRules);
            for (var i = searchRange.StartIdx; i < searchRange.EndIdx && i < ctx.SampledEvents.Count; i++)
            {
                var sampledEvent = ctx.SampledEvents[i];
                if (sampledEvent.IsIgnored || sampledEvent.IsGraphEvent)
                {
                    continue;
                }

                // Skip events from inactive branch if so requested
                if (ignoreInactiveEvents && !sampledEvent.IsFromActiveBranch)
                {
                    continue;
                }

                if (EventSearch.TryGetTransitionEvent(sampledEvent, out var eventMarker, out var optionalID))
                {
                    // Check if we need to match a specific transition ID
                    if (RequireRuleID.IsValid && RequireRuleID != optionalID)
                    {
                        continue;
                    }

                    eventFound = true;

                    // We return the most restrictive marker found
                    if (eventMarker > mostRestrictiveMarkerFound)
                    {
                        mostRestrictiveMarkerFound = eventMarker;
                    }
                }
            }

            if (!eventFound)
            {
                return false;
            }

            return RuleCondition switch
            {
                TransitionRuleCondition.AnyAllowed => mostRestrictiveMarkerFound != TransitionRule.BlockTransition,
                TransitionRuleCondition.FullyAllowed => mostRestrictiveMarkerFound == TransitionRule.AllowTransition,
                TransitionRuleCondition.ConditionallyAllowed => mostRestrictiveMarkerFound == TransitionRule.ConditionallyAllowTransition,
                TransitionRuleCondition.Blocked => mostRestrictiveMarkerFound == TransitionRule.BlockTransition,
                _ => false,
            };
        }
    }

    // A virtual parameter is a graph-computed sub-expression, not an externally set value: it simply
    // evaluates its child node (cached once per graph update).
    partial class VirtualParameterBoolNode
    {
        BoolValueNode ChildNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        // Caching is handled once-per-update by the BoolValueNode base.
        protected override bool GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }

    partial class IsTargetSetNode
    {
        TargetValueNode InputValueNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override bool GetValueInternal(GraphContext ctx) => InputValueNode.GetValue(ctx).IsSet;
    }
}
