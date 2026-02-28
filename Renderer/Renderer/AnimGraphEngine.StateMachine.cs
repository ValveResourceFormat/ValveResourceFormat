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

        StateIndex ActiveStateIndex = -1;
        TransitionNode? ActiveTransition;
        StateInfo[] States;

        public override bool IsValid => base.IsValid && ActiveStateIndex >= 0 && ActiveStateIndex < States.Length;
        public StateInfo ActiveState => States[ActiveStateIndex];
        public StateNode ActiveStateNode => ActiveState.StateNode;

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

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
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
                result = activeState.StateNode.Update(ctx);
                Duration = activeState.StateNode.Duration;
                PreviousTime = activeState.StateNode.PreviousTime;
                CurrentTime = activeState.StateNode.CurrentTime;
            }
            else // Update the transition
            {
                result = ActiveTransition.Update(ctx);
                Duration = ActiveTransition.Duration;
                PreviousTime = ActiveTransition.PreviousTime;
                CurrentTime = ActiveTransition.CurrentTime;
            }

            if (ctx.BranchState is BranchState.Active)
            {
                EvaluateTransitions(ctx, result);
            }

            return result;
        }

        private void EvaluateTransitions(GraphContext ctx, GraphPoseNodeResult currentResult)
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
                return;
            }

            //-------------------------------------------------------------------------
            // Start new transition
            //-------------------------------------------------------------------------
            Debug.Assert(transitionIdx >= 0 && transitionIdx < activeState.Transitions.Length);
            var selectedTransition = activeState.Transitions[transitionIdx];

            // Initialize target state based on transition settings and what the source is (state or transition)
            TransitionNode.StartOptions startOptions = new(currentResult)
            {
                //UpdateRange = pUpdateRange,
                IsSourceTransition = (ActiveTransition != null),
                SourceNode = ActiveTransition != null ? (PoseNode)ActiveTransition : (PoseNode)ActiveState.StateNode,
                //StartCachingSourcePose = selectedTransition.TransitionNode.CacheSourcePose
                //SourceTasksStartMarker = ctx.TaskIndexMarker
            };

            selectedTransition.TransitionNode.InitializeTargetStateAndUpdateTransition(ctx, startOptions);

            ActiveTransition = selectedTransition.TransitionNode;

            // Update state data to that of the new active state
            ActiveStateIndex = selectedTransition.TargetStateIndex;

            Duration = ActiveState.StateNode.Duration;
            PreviousTime = ActiveState.StateNode.PreviousTime;
            CurrentTime = ActiveState.StateNode.CurrentTime;
        }
    }
}
