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
        public bool IsTransitioning => Transition != TransitionState.None;


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


        public void StartTransitionIn(GraphContext ctx)
        {
            Transition = TransitionState.TransitioningIn;
        }

        public void StartTransitionOut(GraphContext ctx)
        {
            Transition = TransitionState.TransitioningOut;

            // Since we update states before we register transitions, we need to ignore all previously sampled state events for this frame
            // This would require sampled events buffer - for now just resample

            // Resample state events with new transition state
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

    partial class TransitionNode
    {
        enum SourceType
        {
            State,
            Transition,
            CachedPose,
        }

        // Set of options for this transition, they are stored as flag since we want to save space
        // Note: not all options can be used together, the tools node will provide validation of the options
        [Flags]
        public enum TransitionOptions_t : byte
        {
            ClampDuration,

            Synchronized, // The time control mode: either sync, match or none
            MatchSourceTime, // The time control mode: either sync, match or none

            MatchSyncEventIndex, // Only checked if MatchSourceTime is set
            MatchSyncEventID, // Only checked if MatchSourceTime is set
            MatchSyncEventPercentage, // Only checked if MatchSourceTime is set

            PreferClosestSyncEventID, // Only checked if MatchSyncEventID is set, will prefer the closest matching sync event rather than the first found
        };

        public struct StartOptions(GraphPoseNodeResult sourceNodeResult)
        {
            public GraphPoseNodeResult SourceNodeResult = sourceNodeResult;
            //public SyncTrackTimeRange UpdateRange;
            public sbyte SourceTasksStartMarker = -1;
            public PoseNode SourceNode;
            public bool IsSourceTransition;
            public bool StartCachingSourcePose;
        };

        PoseNode? SourceNode;
        StateNode TargetStateNode;
        FloatValueNode? DurationOverrideNode;
        FloatValueNode? EventOffsetOverrideNode;
        BoneMaskValueNode? StartBoneMaskNode;
        IDValueNode? TargetSyncIDNode;
        // synctrack
        float TransitionProgress;
        float TransitionDuration; // This is either time in seconds, or percentage of the sync track
        float SyncEventOffset;
        float BlendWeight;
        float BlendedDuration;
        SourceType Type;
        BoneMaskTaskList BoneMaskTaskList;

        public bool IsSourceAState => Type == SourceType.State;
        public bool IsSourceTransition => Type == SourceType.Transition;
        public bool IsSourceACachedPose => Type == SourceType.CachedPose;
        public float ProgressPercentage => TransitionProgress;

        public bool GetOption(TransitionOptions_t option)
        {
            return TransitionOptions.IsFlagSet((uint)option);
        }

        public TransitionNode GetSourceTransitionNode()
        {
            Debug.Assert(IsSourceTransition && SourceNode is TransitionNode);
            return (TransitionNode)SourceNode!;
        }

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);

            ctx.SetNodeFromIndex(TargetStateNodeIdx, ref TargetStateNode);
            ctx.SetOptionalNodeFromIndex(DurationOverrideNodeIdx, ref DurationOverrideNode);
            ctx.SetOptionalNodeFromIndex(TimeOffsetOverrideNodeIdx, ref EventOffsetOverrideNode);
            ctx.SetOptionalNodeFromIndex(StartBoneMaskNodeIdx, ref StartBoneMaskNode);
            ctx.SetOptionalNodeFromIndex(TargetSyncIDNodeIdx, ref TargetSyncIDNode);
        }

        public GraphPoseNodeResult InitializeTargetStateAndUpdateTransition(GraphContext ctx, StartOptions options)
        {
            Debug.Assert(options.SourceNode != null);
            Debug.Assert(SourceNode == null);

            SourceNode = options.SourceNode;
            Type = options.IsSourceTransition ? SourceType.Transition : SourceType.State;

            if (options.StartCachingSourcePose)
            {
                // Start caching source pose
                // ...
            }

            GraphPoseNodeResult result;

            // Copy the source node result as we are gonna potentially modify both the sampled event indices and the task idx
            GraphPoseNodeResult sourceNodeResult = options.SourceNodeResult;
            GraphPoseNodeResult targetNodeResult;

            // Starting a transition out may generate additional graph events so we need to update the sampled event range
            //...
            
            /*
            GraphLayerContext targetLayerContext;
            GraphLayerContext* pSourceLayerCtx = nullptr;
            if ( context.IsInLayer() )
            {
                pSourceLayerCtx = context.m_pLayerContext;
                context.m_pLayerContext = &targetLayerContext;
            }
            */

            // bunch of sync stuff

            // Unsynchronized Transition
            //-------------------------------------------------------------------------

            // Should we clamp how long the transition is active for?
            if (GetOption(TransitionOptions_t.ClampDuration))
            {
                var sourceDuration = SourceNode.Duration;
                if (sourceDuration > 0f)
                {
                    var remainingTime = (1f - SourceNode.CurrentTime) * sourceDuration;
                    TransitionDuration = Math.Min(TransitionDuration, remainingTime);
                }
            }

            // If we have a sync offset or we need to match the source state time, we need to create a target state initial time
            // ...

            // Regular time update (not matched or has sync offset)
            {
                // Transition out - this will resample any source state events so that the target state machine has all the correct state events
                //StartTransitionOutForSource();

                //TargetNode->Initialize( context, SyncTrackTime() );

                // Start transition in and update target
                TargetStateNode.StartTransitionIn(ctx);
                targetNodeResult = TargetStateNode.Update(ctx);
            }

            // Calculate the blend weight, register pose task and update layer weights
            //-------------------------------------------------------------------------

            CalculateBlendWeight();
            //RegisterPoseTasksAndUpdateRootMotion( context, sourceNodeResult, targetNodeResult, result );

            if (ctx.IsInLayer)
            {
                // Calculate the new layer weights based on the transition progress
                //UpdateLayerContext( pSourceLayerCtx, &targetLayerContext );

                // Restore original context
                //context.m_pLayerContext = pSourceLayerCtx;
            }

            // Update transition ( to get initial pose from source )
            //return Update(ctx);
            return sourceNodeResult;
        }

        public bool IsComplete(GraphContext ctx)
        {
            if (TransitionDuration <= 0f)
            {
                return true;
            }

            return TransitionProgress + (ctx.DeltaTime / TransitionDuration) >= 1f;
        }

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            var result = base.Update(ctx);

            if (TransitionDuration <= 0f)
            {
                // Instant transition - just return target
                result = TargetStateNode.Update(ctx);
                TransitionProgress = 1f;
                BlendWeight = 1f;
                return result;
            }

            // Check if source transition is complete
            if (IsSourceTransition && GetSourceTransitionNode().IsComplete(ctx))
            {
                EndSourceTransition(ctx);
            }

            // Update transition progress
            TransitionProgress += ctx.DeltaTime / TransitionDuration;
            TransitionProgress = MathUtils.Saturate(TransitionProgress);

            // Calculate blend weight with easing
            CalculateBlendWeight();

            // Update source and target states
            GraphPoseNodeResult sourceNodeResult;
            if (IsSourceACachedPose)
            {
                // TODO: Read from cached pose
                sourceNodeResult = base.Update(ctx);
            }
            else
            {
                Debug.Assert(SourceNode != null);
                sourceNodeResult = SourceNode.Update(ctx);
            }

            var targetNodeResult = TargetStateNode.Update(ctx);

            // Blend poses
            Blender.Blend(
                sourceNodeResult.Pose,
                targetNodeResult.Pose,
                BlendWeight,
                result.Pose);

            // Blend root motion
            result.RootMotionDelta = Blender.BlendRootMotion(
                sourceNodeResult.RootMotionDelta,
                targetNodeResult.RootMotionDelta,
                BlendWeight,
                RootMotionBlend);

            // Calculate blended duration
            BlendedDuration = MathUtils.Lerp(SourceNode?.Duration ?? 0f, TargetStateNode.Duration, BlendWeight);

            return result;
        }

        void CalculateBlendWeight()
        {
            if (TransitionDuration == 0f)
            {
                BlendWeight = 1f;
            }
            else
            {
                BlendWeight = Easing.Evaluate(BlendWeightEasing, TransitionProgress);
                BlendWeight = MathUtils.Saturate(BlendWeight);
            }
        }

        void EndSourceTransition(GraphContext ctx)
        {
            Debug.Assert(IsSourceTransition);
            var sourceTransition = GetSourceTransitionNode();

            // Replace source with the source transition's target
            SourceNode = sourceTransition.TargetStateNode;
            Type = SourceType.State;
        }
    }
}
