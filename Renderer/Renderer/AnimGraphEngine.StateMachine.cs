using System.Diagnostics;

using StateIndex = System.Int16;

namespace ValveResourceFormat.Renderer.AnimLib
{
    sealed partial class StateMachineNode
    {
        // some InlineVector usage here

        public struct TransitionInfo
        {
            public TransitionNode TransitionNode;
            public BoolValueNode ConditionNode;
            public StateIndex TargetStateIndex;
            public bool CanBeForced;
        }

        public struct StateInfo
        {
            public StateNode StateNode;
            public BoolValueNode? EntryConditionNode;
            public TransitionInfo[] Transitions;
            public bool HasForceableTransitions;

        }

        public StateIndex ActiveStateIndex { get; private set; } = -1;
        public TransitionNode? ActiveTransition { get; private set; }
        public StateInfo[] States;

        // Scratch for EvaluateTransitions (cleared per use, no per-frame allocation)
        readonly List<StateNode> forceableTargetStatesUsingCachedPoses = [];

        // Matches Esoterica: a state machine is valid when it has a valid active state index;
        // the active state's own validity is deliberately not consulted.
        public override bool IsValid => base.IsValid && ActiveStateIndex >= 0 && ActiveStateIndex < States.Length;
        public StateInfo ActiveState => States[ActiveStateIndex];
        public StateNode ActiveStateNode => ActiveState.StateNode;

        public override SyncTrack SyncTrack => ActiveTransition != null
            ? ActiveTransition.SyncTrack
            : ActiveStateIndex >= 0 && ActiveStateIndex < States.Length ? ActiveStateNode.SyncTrack : SyncTrack.Default;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);

            States = new StateInfo[StateDefinitions.Length];

            var s = 0;
            foreach (var stateDefinition in StateDefinitions)
            {
                ctx.SetNodeFromIndex(stateDefinition.StateNodeIdx, ref States[s].StateNode);
                ctx.SetOptionalNodeFromIndex(stateDefinition.EntryConditionNodeIdx, ref States[s].EntryConditionNode);

                States[s].Transitions = new TransitionInfo[stateDefinition.TransitionDefinitions.Length];

                var t = 0;
                foreach (var transitionDefinition in stateDefinition.TransitionDefinitions)
                {
                    ctx.SetNodeFromIndex(transitionDefinition.TransitionNodeIdx, ref States[s].Transitions[t].TransitionNode);
                    ctx.SetNodeFromIndex(transitionDefinition.ConditionNodeIdx, ref States[s].Transitions[t].ConditionNode);

                    States[s].Transitions[t].TargetStateIndex = transitionDefinition.TargetStateIdx;
                    States[s].Transitions[t].CanBeForced = transitionDefinition.CanBeForced;

                    States[s].HasForceableTransitions |= transitionDefinition.CanBeForced;
                    t++;
                }
                s++;
            }

            ActiveStateIndex = DefaultStateIndex;
            Debug.Assert(ActiveStateIndex != -1);

            var activeState = ActiveState.StateNode;

            Duration = activeState.Duration;
            PreviousTime = activeState.PreviousTime;
            CurrentTime = activeState.CurrentTime;
        }

        /// <summary>
        /// Picks the state to start in: the first state whose entry condition passes, or the default.
        /// </summary>
        private StateIndex SelectStartingState(GraphContext ctx)
        {
            for (StateIndex i = 0; i < States.Length; i++)
            {
                if (States[i].EntryConditionNode?.GetValue(ctx) == true)
                {
                    return i;
                }
            }

            return DefaultStateIndex;
        }

        public override void Restart(GraphContext ctx)
        {
            base.Restart(ctx);

            // At graph construction time the containing state restarts us before our own Initialize ran.
            if (States == null)
            {
                return;
            }

            ActiveTransition = null;
            ActiveStateIndex = SelectStartingState(ctx);

            var activeState = ActiveState.StateNode;
            activeState.Restart(ctx);

            Duration = activeState.Duration;
            PreviousTime = activeState.PreviousTime;
            CurrentTime = activeState.CurrentTime;
        }

        private bool hasSelectedStartingState;

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            // The starting state honors entry conditions, evaluated lazily on the first update —
            // at construction time the condition nodes are not initialized yet (node array order).
            if (!hasSelectedStartingState)
            {
                hasSelectedStartingState = true;
                var startingState = SelectStartingState(ctx);

                if (startingState != ActiveStateIndex)
                {
                    ActiveStateIndex = startingState;
                    ActiveStateNode.Restart(ctx);
                }
            }

            var result = base.Update(ctx);

            // Check active transition
            if (ActiveTransition != null && ActiveTransition.IsComplete(ctx))
            {
                // Clear transition flags from target
                ActiveTransition.Stop(ctx);
                ActiveTransition = null;
            }

            // If we are fully in a state, update the state directly
            if (ActiveTransition == null)
            {
                var activeState = ActiveState;
                result = activeState.StateNode.Update(ctx, updateRange);
                Duration = activeState.StateNode.Duration;
                PreviousTime = activeState.StateNode.PreviousTime;
                CurrentTime = activeState.StateNode.CurrentTime;
            }
            else // Update the transition
            {
                result = ActiveTransition.Update(ctx, updateRange);
                Duration = ActiveTransition.Duration;
                PreviousTime = ActiveTransition.PreviousTime;
                CurrentTime = ActiveTransition.CurrentTime;
            }

            if (ctx.BranchState is BranchState.Active)
            {
                result = EvaluateTransitions(ctx, result, updateRange);
            }

            return result;
        }

        private GraphPoseNodeResult EvaluateTransitions(GraphContext ctx, GraphPoseNodeResult currentResult, SyncTrackTimeRange? updateRange)
        {
            var activeState = ActiveState;

            //-------------------------------------------------------------------------
            // Check for a valid transition
            //-------------------------------------------------------------------------

            var transitionIdx = -1;
            for (var i = 0; i < activeState.Transitions.Length; i++)
            {
                var transition = activeState.Transitions[i];
                Debug.Assert(transition.TargetStateIndex != -1);

                // Disallow any transitions to already transitioning states
                // unless this is a forced transition; this prevents infinite loops.
                if (!transition.CanBeForced && States[transition.TargetStateIndex].StateNode.IsTransitioning)
                {
                    continue;
                }

                // Check if the conditions for this transition are satisfied, if they are start a new transition
                if (transition.ConditionNode.GetValue(ctx))
                {
                    transitionIdx = i;
                    break;
                }
            }

            if (transitionIdx == -1)
            {
                return currentResult;
            }

            //-------------------------------------------------------------------------
            // Start new transition
            //-------------------------------------------------------------------------
            Debug.Assert(transitionIdx >= 0 && transitionIdx < activeState.Transitions.Length);
            var selectedTransition = activeState.Transitions[transitionIdx];

            // Handle forced transitions: collect the target states of the new target state's
            // forceable transitions - an in-flight transition to/from one of these must start
            // caching its pose (or switch to it) so no state is updated twice in one frame.
            forceableTargetStatesUsingCachedPoses.Clear();
            var targetStateInfo = States[selectedTransition.TargetStateIndex];
            if (targetStateInfo.HasForceableTransitions)
            {
                foreach (var transitionInfo in targetStateInfo.Transitions)
                {
                    if (transitionInfo.CanBeForced)
                    {
                        forceableTargetStatesUsingCachedPoses.Add(States[transitionInfo.TargetStateIndex].StateNode);
                    }
                }
            }

            // Check if we have a forceable transition back to our current state, if so we need to immediately start caching the source pose
            var startCachingSourcePose = forceableTargetStatesUsingCachedPoses.Contains(activeState.StateNode);

            // Notify the current transition of the new transition about to start
            ActiveTransition?.NotifyNewTransitionStarting(ctx, targetStateInfo.StateNode, forceableTargetStatesUsingCachedPoses);

            // Initialize target state based on transition settings and what the source is (state or transition)
            TransitionNode.StartOptions startOptions = new(currentResult)
            {
                IsSourceTransition = (ActiveTransition != null),
                SourceNode = ActiveTransition != null ? (PoseNode)ActiveTransition : (PoseNode)ActiveState.StateNode,
                UpdateRange = updateRange,
                StartCachingSourcePose = startCachingSourcePose,
            };

            var transitionResult = selectedTransition.TransitionNode.InitializeTargetStateAndUpdateTransition(ctx, startOptions);

            ActiveTransition = selectedTransition.TransitionNode;

            // Update state data to that of the new active state
            ActiveStateIndex = selectedTransition.TargetStateIndex;

            Duration = ActiveState.StateNode.Duration;
            PreviousTime = ActiveState.StateNode.PreviousTime;
            CurrentTime = ActiveState.StateNode.CurrentTime;

            return transitionResult;
        }
    }
}
