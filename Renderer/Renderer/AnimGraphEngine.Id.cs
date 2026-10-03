using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class IDValueNode
    {
        GlobalSymbol cachedValue;

        // Returns the node's value, evaluating it at most once per graph update (matches the C++ WasUpdated guard).
        public GlobalSymbol GetValue(GraphContext ctx)
        {
            if (!WasUpdated(ctx))
            {
                MarkNodeActive(ctx);
                cachedValue = GetValueInternal(ctx);
            }

            return cachedValue;
        }

        protected virtual GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            ctx.LogNodeNotImplemented(NodeIdx, GetType().Name);
            return default;
        }
    }

    partial class CachedIDNode
    {
        IDValueNode InputValueNode;
        GlobalSymbol CachedValue;
        bool HasCachedValue;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(InputValueNodeIdx, ref InputValueNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);

            InputValueNode.Initialize(ctx);

            // Cache on entry
            if (Mode == CachedValueMode.OnEntry)
            {
                CachedValue = InputValueNode.GetValue(ctx);
                HasCachedValue = true;
            }
            else
            {
                HasCachedValue = false;
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            InputValueNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
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

    partial class ConstIDNode
    {
        protected override GlobalSymbol GetValueInternal(GraphContext ctx) => Value;
    }

    partial class ControlParameterIDNode
    {
        string parameterName;

        public override void Instantiate(GraphContext ctx)
        {
            Debug.Assert(NodeIdx >= 0 && NodeIdx < ctx.Graph.ParameterNames.Length);
            parameterName = ctx.Graph.ParameterNames[NodeIdx];
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            return new GlobalSymbol(ctx.Graph.IdParameters[parameterName]);
        }
    }

    // Returns the ID of the sync event the source state is currently in.
    partial class CurrentSyncEventIDNode
    {
        StateNode SourceStateNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            var syncTrack = SourceStateNode.SyncTrack;
            var currentSyncTime = syncTrack.GetTime(SourceStateNode.CurrentTime);
            return syncTrack.GetEventID(currentSyncTime.EventIdx);
        }
    }

    // Selects between two ID inputs (or constants) based on a bool input.
    partial class IDSwitchNode
    {
        BoolValueNode SwitchValueNode;
        IDValueNode? TrueValueNode;
        IDValueNode? FalseValueNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(SwitchValueNodeIdx, ref SwitchValueNode);
            ctx.SetOptionalNodeFromIndex(TrueValueNodeIdx, ref TrueValueNode);
            ctx.SetOptionalNodeFromIndex(FalseValueNodeIdx, ref FalseValueNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);
            SwitchValueNode.Initialize(ctx);
            TrueValueNode?.Initialize(ctx);
            FalseValueNode?.Initialize(ctx);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            SwitchValueNode.Shutdown(ctx);
            TrueValueNode?.Shutdown(ctx);
            FalseValueNode?.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            if (SwitchValueNode.GetValue(ctx))
            {
                return TrueValueNode?.GetValue(ctx) ?? TrueValue;
            }

            return FalseValueNode?.GetValue(ctx) ?? FalseValue;
        }
    }

    // Returns the value of the first passing condition, or the default.
    partial class IDSelectorNode
    {
        BoolValueNode[] ConditionNodes;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref ConditionNodes);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);

            foreach (var node in ConditionNodes)
            {
                node.Initialize(ctx);
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            foreach (var node in ConditionNodes)
            {
                node.Shutdown(ctx);
            }

            base.ShutdownInternal(ctx);
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            for (var i = 0; i < ConditionNodes.Length; i++)
            {
                if (ConditionNodes[i].GetValue(ctx))
                {
                    return Values[i];
                }
            }

            return DefaultValue;
        }
    }

    // Returns the ID of the best matching sampled ID animation event this update, or the default
    // (Esoterica IDEventNode).
    partial class IDEventNode
    {
        StateNode? SourceStateNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            GlobalSymbol foundEventID = default;
            var foundPercentageThrough = 0f;
            var highestWeightFound = -1f;
            var eventFound = false;

            var searchRange = EventSearch.CalculateSearchRange(ctx, SourceStateNode, EventConditionRules);
            var ignoreInactiveEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.IgnoreInactiveEvents);
            var preferHigherWeight = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.PreferHighestWeight);

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

                if (sampledEvent.AnimEvent is not ValveResourceFormat.ResourceTypes.ModelAnimation2.NmIDEvent)
                {
                    continue;
                }

                // If we already have a found event then apply the priority rule
                var updateEvent = !eventFound
                    || (preferHigherWeight
                        ? sampledEvent.Weight >= highestWeightFound
                        : sampledEvent.PercentageThrough >= foundPercentageThrough);

                if (updateEvent)
                {
                    foundEventID = sampledEvent.ID;
                    eventFound = true;
                    foundPercentageThrough = sampledEvent.PercentageThrough;
                    highestWeightFound = sampledEvent.Weight;
                }
            }

            return eventFound ? foundEventID : DefaultValue;
        }
    }

    // Returns the sync ID of the best matching sampled foot event this update (Esoterica FootstepEventIDNode).
    partial class FootstepEventIDNode
    {
        StateNode? SourceStateNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(SourceStateNodeIdx, ref SourceStateNode);
        }

        protected override GlobalSymbol GetValueInternal(GraphContext ctx)
        {
            GlobalSymbol foundID = default;
            var foundPercentageThrough = 0f;
            var highestWeightFound = -1f;
            var eventFound = false;

            var searchRange = EventSearch.CalculateSearchRange(ctx, SourceStateNode, EventConditionRules);
            var ignoreInactiveEvents = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.IgnoreInactiveEvents);
            var preferHigherWeight = EventConditionRules.IsRuleSet(AnimLib.EventConditionRules.PreferHighestWeight);

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

                if (!EventSearch.TryGetFootPhase(sampledEvent, out var phase))
                {
                    continue;
                }

                var updateEvent = !eventFound
                    || (preferHigherWeight
                        ? sampledEvent.Weight >= highestWeightFound
                        : sampledEvent.PercentageThrough >= foundPercentageThrough);

                if (updateEvent)
                {
                    eventFound = true;
                    foundPercentageThrough = sampledEvent.PercentageThrough;
                    highestWeightFound = sampledEvent.Weight;
                    foundID = EventSearch.FootPhaseSyncIDs[(int)phase];
                }
            }

            return foundID;
        }
    }

    // A virtual parameter is a graph-computed sub-expression: evaluates its child (cached once per update).
    partial class VirtualParameterIDNode
    {
        IDValueNode ChildNode;

        public override void Instantiate(GraphContext ctx)
        {
            ctx.SetNodeFromIndex(ChildNodeIdx, ref ChildNode);
        }

        protected override void InitializeInternal(GraphContext ctx)
        {
            base.InitializeInternal(ctx);
            ChildNode.Initialize(ctx);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            ChildNode.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        // Caching is handled once-per-update by the IDValueNode base.
        protected override GlobalSymbol GetValueInternal(GraphContext ctx) => ChildNode.GetValue(ctx);
    }
}
