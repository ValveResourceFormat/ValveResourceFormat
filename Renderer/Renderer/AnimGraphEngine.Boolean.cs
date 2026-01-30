using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class BoolValueNode { public virtual bool GetValue(GraphContext ctx) => throw new NotImplementedException(); }

    partial class ConstBoolNode
    {
        public void Initialize(GraphContext ctx)
        {
            // No initialization needed for ConstBoolNode
        }

        public override bool GetValue(GraphContext ctx) => Value;
    }

    partial class ControlParameterBoolNode
    {
        string parameterName;

        public void Initialize(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Controller.ParameterNames.Length);
            parameterName = ctx.Controller.ParameterNames[NodeIdx];
        }

        public override bool GetValue(GraphContext ctx)
        {
            return ctx.Controller.BoolParameters[parameterName];
        }
    }

    partial class CachedBoolNode
    {
        BoolValueNode InputValueNode;
        bool CachedValue;
        bool HasCachedValue;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override bool GetValue(GraphContext ctx)
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

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref conditionNodes);
        }

        public override bool GetValue(GraphContext ctx)
        {
            return conditionNodes.All(node => node.GetValue(ctx));
        }
    }

    partial class FloatComparisonNode
    {
        FloatValueNode InputNode;
        FloatValueNode? ComparandNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputNode);
            ctx.SetOptionalNodeFromIndex(ComparandValueNodeIdx, ref ComparandNode);
        }

        public override bool GetValue(GraphContext ctx)
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

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override bool GetValue(GraphContext ctx)
        {
            var inputValue = InputValueNode.GetValue(ctx);
            var inRangeInclusive = inputValue >= Range.Min && inputValue <= Range.Max;
            var inRangeExclusive = inputValue > Range.Min && inputValue < Range.Max;

            return IsInclusiveCheck ? inRangeInclusive : inRangeExclusive;
        }
    }

    partial class FootEventConditionNode
    {
        //
    }

    partial class GraphEventConditionNode
    {
        //
    }

    partial class IDComparisonNode
    {
        IDValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override bool GetValue(GraphContext ctx)
        {
            var inputValue = InputValueNode.GetValue(ctx);

            var matches = ComparisionIDs.Contains(inputValue);
            return Comparison switch
            {
                IDComparisonNode__Comparison.Matches => matches,
                IDComparisonNode__Comparison.DoesntMatch => !matches,
                _ => false,
            };
        }
    }

    // IDEventConditionNode
    // IDEventPercentageThroughNode
    // IsExternalGraphSlotFilledNode
    // IsExternalPoseSetNode
    // IsInactiveBranchConditionNode
    // IsTargetSetNode

    partial class NotNode
    {
        BoolValueNode InputValueNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        public override bool GetValue(GraphContext ctx)
        {
            return !InputValueNode.GetValue(ctx);
        }
    }

    partial class OrNode
    {
        BoolValueNode[] conditionNodes;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref conditionNodes);
        }

        public override bool GetValue(GraphContext ctx)
        {
            return conditionNodes.Any(node => node.GetValue(ctx));
        }
    }

    partial class StateCompletedConditionNode
    {
        StateNode SourceStateNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        public override bool GetValue(GraphContext ctx)
        {
            var sourceDuration = SourceStateNode.Duration;

            if (sourceDuration == TimeSpan.Zero)
            {
                return true;
            }
            else
            {
                var transitionTime = TransitionDurationSeconds / sourceDuration.TotalSeconds;
                var transitionPoint = 1.0f - transitionTime;
                return SourceStateNode.CurrentTime >= transitionPoint;
            }
        }
    }

    // SyncEventIndexConditionNode

    partial class TimeConditionNode
    {
        StateNode SourceStateNode;
        FloatValueNode? InputValueNode;

        public void Initialize(GraphContext ctx)
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

        public override bool GetValue(GraphContext ctx)
        {
            var comparisonValue = InputValueNode?.GetValue(ctx) ?? Comparand;

            return Type switch
            {
                TimeConditionNode__ComparisonType.PercentageThroughState => Compare(SourceStateNode.CurrentTime, comparisonValue),
                TimeConditionNode__ComparisonType.PercentageThroughSyncEvent => Compare((float)SourceStateNode.ElapsedTimeInState.TotalSeconds, comparisonValue),
                TimeConditionNode__ComparisonType.ElapsedTime => Compare(SourceStateNode.CurrentTime, comparisonValue),
                // LoopCount?
                _ => false,
            };
        }
    }

    partial class TransitionEventConditionNode
    {
        StateNode? SourceStateNode;

        public void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        public override bool GetValue(GraphContext ctx)
        {
            var eventFound = false;
            var mostRestrictiveMarkerFound = TransitionRule.AllowTransition;

            var ignoreInactiveEvents = EventConditionRules.IsFlagSet((uint)AnimLib.EventConditionRules.IgnoreInactiveEvents);

            // Calculate search range
            /*
            var sampledEventsBuffer = ctx.SampledEventsBuffer;
            var searchRange = CalculateSearchRange(SourceStateNode, sampledEventsBuffer, rules); // TODO: implement or adjust
            for (int i = searchRange.StartIdx; i < searchRange.EndIdx; i++)
            {
                var sampledEvent = sampledEventsBuffer.GetEvent(i);
                if (sampledEvent.IsIgnored() || sampledEvent.IsGraphEvent())
                {
                    continue;
                }

                // Skip events from inactive branch if so requested
                if (ignoreInactiveEvents && !sampledEvent.IsFromActiveBranch())
                {
                    continue;
                }

                var transitionEvent = sampledEvent.TryGetEvent<TransitionEvent>();
                if (transitionEvent != null)
                {
                    // Check if we need to match a specific transition ID
                    if (pDefinition.m_requireRuleID.IsValid())
                    {
                        if (pDefinition.m_requireRuleID != transitionEvent.GetOptionalID())
                        {
                            continue;
                        }
                    }

                    eventFound = true;
                    var eventMarker = transitionEvent.GetRule();

                    // We return the most restrictive marker found
                    if (eventMarker > mostRestrictiveMarkerFound)
                    {
                        mostRestrictiveMarkerFound = eventMarker;
                    }
                }
            }
            */

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

    partial class VirtualParameterBoolNode
    {
        string parameterName;

        public void Initialize(GraphContext ctx)
        {
            // todo: virtual params use different collection
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Controller.ParameterNames.Length);
            parameterName = ctx.Controller.ParameterNames[NodeIdx];
        }

        public override bool GetValue(GraphContext ctx)
        {
            return ctx.Controller.BoolParameters[parameterName];
        }
    }
}
