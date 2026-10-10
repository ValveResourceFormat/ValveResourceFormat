namespace ValveResourceFormat.Renderer.AnimLib
{
    sealed partial class StateNode
    {
        public enum TransitionState : byte
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

        public override SyncTrack SyncTrack => ChildNode?.SyncTrack ?? SyncTrack.Default;

        public override bool IsValid => ChildNode?.IsValid ?? false;

        public void SetTransitioningState(TransitionState s) => Transition = s;

        public override void Instantiate(GraphContext ctx)
        {
            // Sizes the pose buffer; an off state without a child returns it directly.
            base.Instantiate(ctx);

            ctx.SetOptionalNodeFromIndex(ChildNodeIdx, ref ChildNode);
            ctx.SetOptionalNodeFromIndex(LayerBoneMaskNodeIdx, ref BoneMaskValueNode);
            ctx.SetOptionalNodeFromIndex(LayerWeightNodeIdx, ref LayerWeightNode);
            ctx.SetOptionalNodeFromIndex(LayerRootMotionWeightNodeIdx, ref LayerRootMotionWeightNode);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            Transition = TransitionState.None;
            SampledEventRange = default;
            ElapsedTimeInState = TimeSpan.Zero;
            PreviousTime = CurrentTime = 0f;
            Duration = 0f;

            if (ChildNode != null)
            {
                ChildNode.Initialize(ctx, initialTime);

                if (ChildNode.IsValid)
                {
                    Duration = ChildNode.Duration;
                    PreviousTime = ChildNode.PreviousTime;
                    CurrentTime = ChildNode.CurrentTime;
                }
            }

            BoneMaskValueNode?.Initialize(ctx);
            LayerWeightNode?.Initialize(ctx);
            // Note: the layer root-motion weight node is not part of the lifecycle upstream either

            // Flag this as the first update for this state, this will cause state entry events to be sampled for at least one update
            IsFirstStateUpdate = true;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            BoneMaskValueNode?.Shutdown(ctx);
            LayerWeightNode?.Shutdown(ctx);
            ChildNode?.Shutdown(ctx);

            Transition = TransitionState.None;
            base.ShutdownInternal(ctx);
        }

        public void StartTransitionIn(GraphContext ctx)
        {
            Transition = TransitionState.TransitioningIn;
        }

        public void StartTransitionOut(GraphContext ctx)
        {
            Transition = TransitionState.TransitioningOut;
        }

        public void StartTransitionOut(GraphContext ctx, bool isZeroDurationTransition)
        {
            Transition = TransitionState.TransitioningOut;

            // The state was updated before the transition was registered; its already-sampled events
            // no longer belong to the active branch.
            ctx.SampledEvents.MarkEventsAsFromInactiveBranch(SampledEventRange);

            // For an instant transition resample the exit events (as inactive-branch events)
            if (isZeroDurationTransition)
            {
                var previousBranchState = ctx.BranchState;
                ctx.BranchState = BranchState.Inactive;
                SampleStateEvents(ctx);
                ctx.BranchState = previousBranchState;
            }
        }

        /// <summary>The range of events this state appended to the buffer during the current update.</summary>
        public SampledEventRange SampledEventRange { get; private set; }

        public void SampleStateEvents(GraphContext ctx)
        {
            var isActiveBranch = ctx.BranchState == BranchState.Active;

            if (IsFirstStateUpdate || (TransitioningIn && isActiveBranch))
            {
                foreach (var entryEventID in EntryEvents)
                {
                    ctx.SampledEvents.EmplaceGraphEvent(NodeIdx, GraphEventType.Entry, entryEventID, isActiveBranch);
                }
            }
            else if (Transition == TransitionState.None && isActiveBranch)
            {
                foreach (var executeEventID in ExecuteEvents)
                {
                    ctx.SampledEvents.EmplaceGraphEvent(NodeIdx, GraphEventType.FullyInState, executeEventID, isActiveBranch);
                }
            }
            else if (TransitioningOut)
            {
                foreach (var exitEventID in ExitEvents)
                {
                    ctx.SampledEvents.EmplaceGraphEvent(NodeIdx, GraphEventType.Exit, exitEventID, isActiveBranch);
                }
            }

            // Sample Timed Events
            // A state can ask for the time it has really been in, as how far its child has played says
            // nothing when the child is a held pose
            var elapsedTime = UseActualElapsedTimeInStateForTimedEvents
                ? (float)ElapsedTimeInState.TotalSeconds
                : Duration * CurrentTime;
            foreach (var timedEvent in TimedElapsedEvents)
            {
                var fire = timedEvent.ComparisionOperator == StateNode.TimedEvent.Comparison.GreaterThanEqual
                    ? elapsedTime >= timedEvent.TimeValueSeconds
                    : elapsedTime <= timedEvent.TimeValueSeconds;

                if (fire)
                {
                    ctx.SampledEvents.EmplaceGraphEvent(NodeIdx, GraphEventType.Timed, timedEvent.ID, isActiveBranch);
                }
            }

            var currentTimeRemaining = (1f - CurrentTime) * Duration;
            foreach (var timedEvent in TimedRemainingEvents)
            {
                var fire = timedEvent.ComparisionOperator == StateNode.TimedEvent.Comparison.GreaterThanEqual
                    ? currentTimeRemaining >= timedEvent.TimeValueSeconds
                    : currentTimeRemaining <= timedEvent.TimeValueSeconds;

                if (fire)
                {
                    ctx.SampledEvents.EmplaceGraphEvent(NodeIdx, GraphEventType.Timed, timedEvent.ID, isActiveBranch);
                }
            }

            // Keep the state's recorded event range covering everything sampled this update
            SampledEventRange = new(SampledEventRange.StartIdx, ctx.SampledEvents.Count);
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
                ctx.LayerContext.Weight *= Math.Clamp(LayerWeightNode?.GetValue(ctx) ?? 1.0f, 0f, 1f);
                ctx.LayerContext.RootMotionWeight *= Math.Clamp(LayerRootMotionWeightNode?.GetValue(ctx) ?? 1.0f, 0f, 1f);
            }

            // Update bone mask task list
            //-------------------------------------------------------------------------
            if (BoneMaskValueNode != null)
            {
                var boneMaskTaskList = BoneMaskValueNode.GetValue(ctx);

                // If we dont have a bone mask task list, use a copy of the state's task list
                if (!ctx.LayerContext.MaskTaskList.HasTasks)
                {
                    ctx.LayerContext.MaskTaskList.CopyFrom(boneMaskTaskList);
                }
                else // If we already have a bone mask set, combine the bone masks
                {
                    ctx.LayerContext.MaskTaskList.CombineWith(boneMaskTaskList);
                }
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var eventRangeStart = ctx.SampledEvents.Count;
            var result = base.Update(ctx);

            if (ChildNode is { IsValid: true })
            {
                result = ChildNode.Update(ctx, updateRange);
                Duration = ChildNode.Duration;
                PreviousTime = ChildNode.PreviousTime;
                CurrentTime = ChildNode.CurrentTime;
            }
            else
            {
                // No pose of its own, which must add nothing inside an additive layer
                ctx.GetDefaultPose().CopyTo(result.Pose, 0);
                result.NoPose = true;
            }

            // track time spent in state
            ElapsedTimeInState += TimeSpan.FromSeconds(ctx.DeltaTime);

            // Sample graph events ( we need to track the sampled range for this node explicitly )
            SampledEventRange = new(eventRangeStart, ctx.SampledEvents.Count);
            SampleStateEvents(ctx);

            // The state's event range covers the child's animation events plus its own graph events.
            SampledEventRange = new(eventRangeStart, ctx.SampledEvents.Count);
            result.SampledEventRange = SampledEventRange;

            // Update layer context and return
            UpdateLayerContext(ctx);
            IsFirstStateUpdate = false;

            return result;
        }
    }
}
