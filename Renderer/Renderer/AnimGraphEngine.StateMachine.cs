using System.Diagnostics;

using StateIndex = System.Int16;

namespace ValveResourceFormat.Renderer.AnimLib
{
    sealed partial class StateMachineNode
    {
        // some InlineVector usage here

        struct TransitionInfo
        {
            public TransitionNode TransitionNode;
            public BoolValueNode ConditionNode;
            public StateIndex TargetStateIndex;
            public bool CanBeForced;
        }

        struct StateInfo
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

            var activeState = States[ActiveStateIndex].StateNode;

            Duration = activeState.Duration;
            PreviousTime = activeState.PreviousTime;
            CurrentTime = activeState.CurrentTime;

            // InitializeTransitionConditions(ctx);
        }

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            var result = base.Update(ctx);

            // If we are fully in a state, update the state directly
            if (ActiveTransition == null)
            {
                var activeState = States[ActiveStateIndex];
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
                // EvaluateTransitions( context, pUpdateRange, result, taskIndexMarker );
            }

            return result;
        }
    }
}
