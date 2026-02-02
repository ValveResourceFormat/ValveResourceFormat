using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    sealed partial class StateNode
    {
        enum TransitionState : byte
        {
            None,
            TransitioningIn,
            TransitioningOut,
        };

        PoseNode? ChildNode;
        BoneMaskValueNode? BoneMaskValueNode;
        FloatValueNode? LayerWeightNode;
        FloatValueNode? LayerRootMotionWeightNode;

        public TimeSpan ElapsedTimeInState;
        TransitionState Transition;
        public bool IsFirstStateUpdate;

        public bool TransitioningIn => Transition == TransitionState.TransitioningIn;
        public bool TransitioningOut => Transition == TransitionState.TransitioningOut;


        public override void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(ChildNodeIdx, ref ChildNode);
            ctx.SetOptionalNodeFromIndex(LayerBoneMaskNodeIdx, ref BoneMaskValueNode);
            ctx.SetOptionalNodeFromIndex(LayerWeightNodeIdx, ref LayerWeightNode);
            ctx.SetOptionalNodeFromIndex(LayerRootMotionWeightNodeIdx, ref LayerRootMotionWeightNode);

            if (ChildNode is not null)
            {
                Duration = ChildNode.Duration;
                PreviousTime = ChildNode.PreviousTime;
                CurrentTime = ChildNode.CurrentTime;
            }

            // Flag this as the first update for this state, this will cause state entry events to be sampled for at least one update
            IsFirstStateUpdate = true;
        }


        public void StartTransitionIn()
        {
            Transition = TransitionState.TransitioningIn;
        }

        public void StartTransitionOut(GraphContext ctx)
        {
            Transition = TransitionState.TransitioningOut;

            // some event code
            // ...

            SampleStateEvents(ctx);
        }

        public void SampleStateEvents(GraphContext ctx)
        {
            var isActiveBranch = ctx.BranchState == BranchState.Active;

            if (IsFirstStateUpdate || (TransitioningIn && isActiveBranch))
            {
                /*
                for ( auto const& entryEventID : pStateDefinition->m_entryEvents )
                {
                    context.m_pSampledEventsBuffer->EmplaceGraphEvent( GetNodeIndex(), GraphEventType::Entry, entryEventID, isInActiveBranch );
                }
                */

                // ...
            }
        }


        public void UpdateLayerContext(GraphContext ctx)
        {
            if (!ctx.IsInLayer)
            {
                return;
            }

            // Update layer weights
            //-------------------------------------------------------------------------
            if (IsOffState)
            {
                ctx.LayerContext.Weight = 0.0f;
                ctx.LayerContext.RootMotionWeight = 0.0f;
            }
            else
            {
                ctx.LayerContext.Weight *= LayerWeightNode?.GetValue(ctx) ?? 1.0f;
                ctx.LayerContext.RootMotionWeight *= LayerRootMotionWeightNode?.GetValue(ctx) ?? 1.0f;
            }

            // Update bone mask task list
            //-------------------------------------------------------------------------
            if (BoneMaskValueNode != null)
            {
                var boneMask = BoneMaskValueNode.GetValue(ctx);
                // task list...
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            var result = base.Update(ctx);

            if (ChildNode != null)
            {
                result = ChildNode.Update(ctx);
                Duration = ChildNode.Duration;
                PreviousTime = ChildNode.PreviousTime;
                CurrentTime = ChildNode.CurrentTime;
                // sampled event range
            }

            // track time spent in state
            ElapsedTimeInState += TimeSpan.FromSeconds(ctx.DeltaTime);

            // Sample graph events ( we need to track the sampled range for this node explicitly )
            SampleStateEvents(ctx);

            // Update layer context and return
            UpdateLayerContext(ctx);
            IsFirstStateUpdate = false;

            return result;
        }
    }
}
