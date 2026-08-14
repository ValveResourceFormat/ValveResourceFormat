using System.Diagnostics;

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
            var elapsedTime = Duration * CurrentTime;
            foreach (var timedEvent in TimedElapsedEvents)
            {
                var fire = timedEvent.ComparisionOperator == StateNode__TimedEvent__Comparison.GreaterThanEqual
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
                var fire = timedEvent.ComparisionOperator == StateNode__TimedEvent__Comparison.GreaterThanEqual
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

    partial class TransitionNode
    {
        enum SourceType
        {
            State,
            Transition,
            CachedPose,
            OffState,
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

            MatchTimeInSeconds, // Only checked if MatchSourceTime is set
            OffsetTimeInSeconds, // Only checked if MatchSourceTime is not set
        };

        public struct StartOptions(GraphPoseNodeResult sourceNodeResult)
        {
            public GraphPoseNodeResult SourceNodeResult = sourceNodeResult;
            public SyncTrackTimeRange? UpdateRange;
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
        SyncTrack? blendedSyncTrack;
        readonly SyncTrack ownedSyncTrack = SyncTrack.CreateBlendScratch();
        float TransitionProgress;
        float TransitionDuration; // This is either time in seconds, or percentage of the sync track
        float SyncEventOffset;
        float BlendWeight;
        float BlendedDuration;
        SourceType Type;
        BoneMaskTaskList BoneMaskTaskList;
        int cachedPoseBufferID = -1;

        // Scratch layer context for the target state (Esoterica swaps context.m_pLayerContext)
        readonly LayerContext targetLayerContext = new();

        public override SyncTrack SyncTrack => blendedSyncTrack ?? TargetStateNode?.SyncTrack ?? SyncTrack.Default;

        public bool IsSourceAState => Type == SourceType.State;
        public bool IsSourceTransition => Type == SourceType.Transition;
        public bool IsSourceACachedPose => Type == SourceType.CachedPose;
        public bool IsSourceAnOffState => Type == SourceType.OffState;
        public bool IsSourceACachedPoseOrOffState => Type is SourceType.CachedPose or SourceType.OffState;
        public float ProgressPercentage => TransitionProgress;

        public bool GetOption(TransitionOptions_t option)
        {
            // The options enum stores bit indices, not masks
            return TransitionOptions.IsFlagSet(1u << (int)option);
        }

        public bool IsSynchronized => GetOption(TransitionOptions_t.Synchronized);


        public StateNode GetSourceStateNode()
        {
            Debug.Assert(IsSourceAState && SourceNode is StateNode);
            return (StateNode)SourceNode!;
        }

        public TransitionNode GetSourceTransitionNode()
        {
            Debug.Assert(IsSourceTransition && SourceNode is TransitionNode);
            return (TransitionNode)SourceNode!;
        }

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);

            ctx.SetNodeFromIndex(TargetStateNodeIdx, ref TargetStateNode);
            ctx.SetOptionalNodeFromIndex(DurationOverrideNodeIdx, ref DurationOverrideNode);
            ctx.SetOptionalNodeFromIndex(TimeOffsetOverrideNodeIdx, ref EventOffsetOverrideNode);
            ctx.SetOptionalNodeFromIndex(StartBoneMaskNodeIdx, ref StartBoneMaskNode);
            ctx.SetOptionalNodeFromIndex(TargetSyncIDNodeIdx, ref TargetSyncIDNode);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            SyncEventOffset = 0f;

            // Reset transition duration and progress; the override value node is only alive for the read
            if (DurationOverrideNode != null)
            {
                DurationOverrideNode.Initialize(ctx);
                TransitionDuration = Math.Clamp(DurationOverrideNode.GetValue(ctx), 0f, 10f);
                DurationOverrideNode.Shutdown(ctx);
            }
            else
            {
                TransitionDuration = DurationSeconds; // From definition (parsed from file)
            }

            TransitionProgress = 0f;
            BlendWeight = 0f;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            // Release cached pose buffers
            if (cachedPoseBufferID != -1)
            {
                ctx.DestroyCachedPose(cachedPoseBufferID);
                cachedPoseBufferID = -1;
            }

            // Clear transition flags from target
            TargetStateNode.SetTransitioningState(StateNode.TransitionState.None);
            CurrentTime = 1f;

            // Shutdown source node
            if (SourceNode != null)
            {
                if (IsSourceTransition)
                {
                    EndSourceTransition(ctx);
                }

                SourceNode.Shutdown(ctx);
                SourceNode = null;
            }
            else
            {
                if (TransitionDuration != 0.0f)
                {
                    Debug.Assert(IsSourceACachedPoseOrOffState);
                }
            }

            base.ShutdownInternal(ctx);
        }

        void StartCachingSourcePose(GraphContext ctx)
        {
            Debug.Assert(cachedPoseBufferID == -1);
            cachedPoseBufferID = ctx.CreateCachedPose();
        }

        /// <summary>
        /// Called before a new (forced) transition starts so this in-flight transition can switch to a
        /// cached-pose source if its source is about to become the new target state, or start caching
        /// its pose if a forceable transition back to its source may follow (Esoterica
        /// TransitionNode::NotifyNewTransitionStarting).
        /// </summary>
        public void NotifyNewTransitionStarting(GraphContext ctx, StateNode targetStateNode, List<StateNode> forceableFutureTargetStatesUsingCachedPoses)
        {
            if (IsSourceTransition)
            {
                var sourceTransitionNode = GetSourceTransitionNode();

                // If the source transition is to the new target state, we need to cancel the transition and use the cached pose
                var sourceTransitionTargetState = sourceTransitionNode.TargetStateNode;
                if (sourceTransitionTargetState == targetStateNode)
                {
                    Type = cachedPoseBufferID != -1 ? SourceType.CachedPose : SourceType.OffState;

                    // We also need to explicitly shutdown the source transition target state as by default we dont shutdown target states when shutting down a transition
                    sourceTransitionTargetState.Shutdown(ctx);

                    // Shutdown the source transition
                    sourceTransitionNode.Shutdown(ctx);
                    SourceNode = null;
                }
                // If the source transition is to a future forceable state, we need to cache the result
                else if (cachedPoseBufferID == -1 && forceableFutureTargetStatesUsingCachedPoses.Contains(sourceTransitionTargetState))
                {
                    StartCachingSourcePose(ctx);
                }
            }
            else if (IsSourceAState)
            {
                if (SourceNode == targetStateNode)
                {
                    var sourceState = GetSourceStateNode();
                    Type = cachedPoseBufferID != -1 ? SourceType.CachedPose : SourceType.OffState;

                    sourceState.Shutdown(ctx);
                    SourceNode = null;
                }
                else if (cachedPoseBufferID == -1 && forceableFutureTargetStatesUsingCachedPoses.Contains(GetSourceStateNode()))
                {
                    StartCachingSourcePose(ctx);
                }
            }
            // else: source is already a cached pose or off state - do nothing

            //-------------------------------------------------------------------------

            // If the source is still a transition node, notify it that we are starting a new transition
            if (IsSourceTransition)
            {
                GetSourceTransitionNode().NotifyNewTransitionStarting(ctx, targetStateNode, forceableFutureTargetStatesUsingCachedPoses);
            }
        }

        /// <summary>
        /// Lerps the source layer context towards the target state's layer context by the transition
        /// blend weight (Esoterica TransitionNode::UpdateLayerContext).
        /// </summary>
        void UpdateLayerContext(LayerContext sourceAndResultLayerContext, LayerContext targetContext)
        {
            // Update layer weights
            //-------------------------------------------------------------------------

            sourceAndResultLayerContext.Weight = MathUtils.Lerp(sourceAndResultLayerContext.Weight, targetContext.Weight, BlendWeight);
            sourceAndResultLayerContext.RootMotionWeight = MathUtils.Lerp(sourceAndResultLayerContext.RootMotionWeight, targetContext.RootMotionWeight, BlendWeight);

            // Update final bone mask
            //-------------------------------------------------------------------------

            if (sourceAndResultLayerContext.MaskTaskList.HasTasks && targetContext.MaskTaskList.HasTasks)
            {
                sourceAndResultLayerContext.MaskTaskList.BlendTo(targetContext.MaskTaskList, BlendWeight);
            }
            else // Only one bone mask is set
            {
                if (sourceAndResultLayerContext.MaskTaskList.HasTasks)
                {
                    // Keep the source mask from the source state while blending out
                    if (TargetStateNode.IsOffState)
                    {
                        // Do nothing
                    }
                    else // Blend to no bone mask (all weights = 1.0f)
                    {
                        sourceAndResultLayerContext.MaskTaskList.BlendToGeneratedMask(1f, BlendWeight);
                    }
                }
                else if (targetContext.MaskTaskList.HasTasks)
                {
                    // Keep the target bone mask on the whole way through the blend
                    if (SourceNode != null && IsSourceAState && GetSourceStateNode().IsOffState)
                    {
                        sourceAndResultLayerContext.MaskTaskList = targetContext.MaskTaskList;
                    }
                    else // Blend from no mask (from all weights = 1.0f)
                    {
                        sourceAndResultLayerContext.MaskTaskList = targetContext.MaskTaskList;
                        sourceAndResultLayerContext.MaskTaskList.BlendFromGeneratedMask(1f, BlendWeight);
                    }
                }
            }
        }

        public GraphPoseNodeResult InitializeTargetStateAndUpdateTransition(GraphContext ctx, StartOptions options)
        {
            Debug.Assert(options.SourceNode != null);
            Debug.Assert(SourceNode == null && IsInitialized);

            SourceNode = options.SourceNode;
            Type = options.IsSourceTransition ? SourceType.Transition : SourceType.State;

            if (options.StartCachingSourcePose)
            {
                StartCachingSourcePose(ctx);
            }

            var sourceNodeResult = options.SourceNodeResult;

            // Layer context update: everything the target state contributes goes into a scratch
            // context which is lerped with the source context at the end.
            LayerContext? sourceLayerContext = null;
            if (ctx.IsInLayer)
            {
                sourceLayerContext = ctx.LayerContext;
                targetLayerContext.Reset();
                ctx.LayerContext = targetLayerContext;
            }

            // Cache source node pose
            if (cachedPoseBufferID != -1 && sourceNodeResult.Pose != null)
            {
                sourceNodeResult.Pose.CopyTo(ctx.GetCachedPoseBuffer(cachedPoseBufferID), 0);
            }

            void StartTransitionOutForSource()
            {
                var isInstantTransition = TransitionDuration == 0f;

                if (Type == SourceType.State)
                {
                    GetSourceStateNode().StartTransitionOut(ctx, isInstantTransition);
                }
                else
                {
                    GetSourceTransitionNode().TargetStateNode.StartTransitionOut(ctx, isInstantTransition);
                }

                if (isInstantTransition)
                {
                    if (IsSourceTransition)
                    {
                        EndSourceTransition(ctx);
                    }

                    // Shutdown the source node
                    SourceNode!.Shutdown(ctx);
                    SourceNode = null;
                }
            }

            GraphPoseNodeResult targetNodeResult;
            SyncTrackTimeRange? targetUpdateRange = null;
            SyncTrack? sourceSyncTrackForBlend = null;

            // Use sync events to initialize the target state
            //-------------------------------------------------------------------------

            if (IsSynchronized || options.UpdateRange != null)
            {
                var sourceSyncTrack = SourceNode!.SyncTrack;
                sourceSyncTrackForBlend = sourceSyncTrack;

                // Calculate the source update sync range
                var sourceUpdateRange = new SyncTrackTimeRange(
                    sourceSyncTrack.GetTime(SourceNode.PreviousTime),
                    sourceSyncTrack.GetTime(SourceNode.CurrentTime));

                // Calculate transition duration
                if (GetOption(TransitionOptions_t.ClampDuration))
                {
                    // Calculate the delta between the current position and the real end of the source
                    var sourceRealEndTime = sourceSyncTrack.GetPercentageThrough(sourceSyncTrack.GetEndTime());
                    var sourceCurrentTime = sourceSyncTrack.GetPercentageThrough(sourceUpdateRange.StartTime);

                    var deltaToRealEnd = sourceRealEndTime > sourceCurrentTime
                        ? sourceRealEndTime - sourceCurrentTime
                        : 1f - (sourceCurrentTime - sourceRealEndTime);

                    // If the end of the source occurs before the transition completes, clamp the duration
                    var sourceDuration = SourceNode.Duration;
                    if (sourceDuration > 0f)
                    {
                        TransitionDuration = Math.Min(TransitionDuration, sourceDuration * deltaToRealEnd);
                    }
                }

                // Only apply the transition's sync offset when we are not part of a synced update
                if (options.UpdateRange == null)
                {
                    if (EventOffsetOverrideNode != null)
                    {
                        EventOffsetOverrideNode.Initialize(ctx);
                        SyncEventOffset = MathF.Floor(EventOffsetOverrideNode.GetValue(ctx));
                        EventOffsetOverrideNode.Shutdown(ctx);
                    }
                    else
                    {
                        SyncEventOffset = MathF.Floor(TimeOffset);
                    }
                }
                else
                {
                    SyncEventOffset = 0f;
                }

                var offset = (int)SyncEventOffset;
                targetUpdateRange = new SyncTrackTimeRange(
                    new SyncTrackTime(sourceUpdateRange.StartTime.EventIdx + offset, sourceUpdateRange.StartTime.PercentageThrough.Value),
                    new SyncTrackTime(sourceUpdateRange.EndTime.EventIdx + offset, sourceUpdateRange.EndTime.PercentageThrough.Value));

                // Transition out, then initialize and synchronize the target
                StartTransitionOutForSource();
                TargetStateNode.Initialize(ctx, targetUpdateRange.Value.StartTime);
                TargetStateNode.StartTransitionIn(ctx);
                targetNodeResult = TargetStateNode.Update(ctx, targetUpdateRange);
            }

            // Unsynchronized Transition
            //-------------------------------------------------------------------------

            else
            {
                // Try get the sync event offset (note this may be in seconds based on the flags)
                if (EventOffsetOverrideNode != null)
                {
                    EventOffsetOverrideNode.Initialize(ctx);
                    SyncEventOffset = EventOffsetOverrideNode.GetValue(ctx);
                    EventOffsetOverrideNode.Shutdown(ctx);
                }
                else
                {
                    SyncEventOffset = TimeOffset;
                }

                // Should we clamp how long the transition is active for?
                var sourceDuration = SourceNode!.Duration;
                if (GetOption(TransitionOptions_t.ClampDuration) && sourceDuration > 0f)
                {
                    var remainingNodeTime = (1f - SourceNode.CurrentTime) * sourceDuration;
                    TransitionDuration = Math.Min(TransitionDuration, remainingNodeTime);
                }

                // If we have a sync offset or need to match the source state time, seed the target time
                var shouldMatchSourceTime = GetOption(TransitionOptions_t.MatchSourceTime);
                if (shouldMatchSourceTime || Math.Abs(SyncEventOffset) > 1e-5f)
                {
                    var sourceCurrentTimeForMatch = SourceNode.CurrentTime;
                    var sourceSyncTrack = SourceNode.SyncTrack;
                    var sourceFromSyncTime = sourceSyncTrack.GetTime(sourceCurrentTimeForMatch);

                    var targetStartEventSyncTime = new SyncTrackTime(0, 0f);

                    var shouldMatchInSeconds = GetOption(TransitionOptions_t.MatchTimeInSeconds);
                    if (shouldMatchInSeconds || GetOption(TransitionOptions_t.OffsetTimeInSeconds))
                    {
                        var sourceCurrentTimeSeconds = shouldMatchInSeconds ? sourceDuration * sourceCurrentTimeForMatch : 0f;
                        var targetDesiredTimeSeconds = sourceCurrentTimeSeconds + SyncEventOffset;
                        SyncEventOffset = 0f;

                        // Transiently initialize the target to read its duration and sync track
                        TargetStateNode.Initialize(ctx, default);
                        var targetDuration = TargetStateNode.Duration;
                        var targetDesiredTime = targetDuration > 0f ? MathUtils.Saturate(targetDesiredTimeSeconds / targetDuration) : 0f;
                        targetStartEventSyncTime = TargetStateNode.SyncTrack.GetTime(targetDesiredTime);
                        TargetStateNode.Shutdown(ctx);
                    }
                    else // Match using sync time
                    {
                        var eventIdx = 0;
                        var percentageThrough = 0f;

                        if (shouldMatchSourceTime)
                        {
                            if (GetOption(TransitionOptions_t.MatchSyncEventIndex))
                            {
                                eventIdx = sourceFromSyncTime.EventIdx;
                            }
                            else if (GetOption(TransitionOptions_t.MatchSyncEventID))
                            {
                                // Get the sync event ID to match; the value node is only alive for the read
                                GlobalSymbol eventIDToMatch;
                                if (TargetSyncIDNode != null)
                                {
                                    TargetSyncIDNode.Initialize(ctx);
                                    eventIDToMatch = TargetSyncIDNode.GetValue(ctx);
                                    TargetSyncIDNode.Shutdown(ctx);
                                }
                                else
                                {
                                    eventIDToMatch = sourceSyncTrack.GetEventID(sourceFromSyncTime.EventIdx);
                                }

                                if (eventIDToMatch.IsValid)
                                {
                                    // Transiently initialize the target to read its sync track
                                    TargetStateNode.Initialize(ctx, targetStartEventSyncTime);
                                    var targetSyncTrack = TargetStateNode.SyncTrack;
                                    eventIdx = GetOption(TransitionOptions_t.PreferClosestSyncEventID)
                                        ? targetSyncTrack.GetClosestEventIndexForID(sourceFromSyncTime, eventIDToMatch)
                                        : targetSyncTrack.GetEventIndexForID(eventIDToMatch);
                                    TargetStateNode.Shutdown(ctx);
                                }
                            }

                            if (GetOption(TransitionOptions_t.MatchSyncEventPercentage))
                            {
                                percentageThrough = sourceFromSyncTime.PercentageThrough.Value;
                            }
                        }

                        // Apply the sync event offset (upstream passes the integer part as the
                        // percentage here, which trips its own validity assert; we keep the fraction)
                        var newIdxAndPercentage = eventIdx + percentageThrough + SyncEventOffset;
                        eventIdx = (int)MathF.Floor(newIdxAndPercentage);
                        percentageThrough = Math.Abs(newIdxAndPercentage - eventIdx);
                        targetStartEventSyncTime = new SyncTrackTime(eventIdx, percentageThrough);
                    }

                    // Transition out, then initialize the target at the computed sync time and update
                    // it with a zero time-step: we dont want to advance the target on this update but
                    // we do want the target pose to be created
                    StartTransitionOutForSource();
                    TargetStateNode.Initialize(ctx, targetStartEventSyncTime);
                    TargetStateNode.StartTransitionIn(ctx);

                    var oldDeltaTime = ctx.DeltaTime;
                    ctx.DeltaTime = 0f;
                    targetNodeResult = TargetStateNode.Update(ctx);
                    ctx.DeltaTime = oldDeltaTime;
                }
                else // Regular start at the beginning of the target
                {
                    StartTransitionOutForSource();
                    TargetStateNode.Initialize(ctx, default);
                    TargetStateNode.StartTransitionIn(ctx);
                    targetNodeResult = TargetStateNode.Update(ctx);
                }
            }

            // Calculate the blend weight and blend the first frame
            //-------------------------------------------------------------------------

            CalculateBlendWeight();

            if (ctx.IsInLayer && sourceLayerContext != null)
            {
                // Calculate the new layer weights based on the transition progress
                UpdateLayerContext(sourceLayerContext, targetLayerContext);

                // Restore original context
                ctx.LayerContext = sourceLayerContext;
            }

            GraphPoseNodeResult result;

            if (SourceNode == null)
            {
                // Instant transition - target only
                result = targetNodeResult;
                PreviousTime = 0f;
                CurrentTime = 0f;
                BlendedDuration = TargetStateNode.Duration;
                blendedSyncTrack = TargetStateNode.SyncTrack;
            }
            else
            {
                result = base.Update(ctx);
                Blender.Blend(sourceNodeResult.Pose, targetNodeResult.Pose, BlendWeight, result.Pose);
                result.RootMotionDelta = Blender.BlendRootMotion(sourceNodeResult.RootMotionDelta, targetNodeResult.RootMotionDelta, BlendWeight, RootMotionBlend);
                result.SampledEventRange = ctx.SampledEvents.BlendEventRanges(sourceNodeResult.SampledEventRange, targetNodeResult.SampledEventRange, BlendWeight);

                if (targetUpdateRange != null && sourceSyncTrackForBlend != null)
                {
                    // Create the blended sync track
                    var targetSyncTrack = TargetStateNode.SyncTrack;
                    ownedSyncTrack.SetToBlendOf(sourceSyncTrackForBlend, targetSyncTrack, BlendWeight);
                    blendedSyncTrack = ownedSyncTrack;
                    BlendedDuration = SyncTrack.CalculateDurationSynchronized(SourceNode.Duration, TargetStateNode.Duration, sourceSyncTrackForBlend.NumEvents, targetSyncTrack.NumEvents, blendedSyncTrack.NumEvents, BlendWeight);
                    PreviousTime = blendedSyncTrack.GetPercentageThrough(targetUpdateRange.Value.StartTime);
                    CurrentTime = blendedSyncTrack.GetPercentageThrough(targetUpdateRange.Value.EndTime);
                }
                else
                {
                    PreviousTime = 0f;
                    CurrentTime = 0f;
                    BlendedDuration = MathUtils.Lerp(SourceNode.Duration, TargetStateNode.Duration, BlendWeight);
                    blendedSyncTrack = TargetStateNode.SyncTrack;
                }
            }

            // Expose the target duration so any "state completed" conditions trigger correctly
            Duration = TargetStateNode.Duration;

            return result;
        }

        public bool IsComplete(GraphContext ctx)
        {
            if (TransitionDuration <= 0f)
            {
                return true;
            }

            return TransitionProgress + (ctx.DeltaTime / TransitionDuration) >= 1f;
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx);

            if (TransitionDuration <= 0f)
            {
                // Instant transition - just return target
                result = TargetStateNode.Update(ctx, updateRange);
                Duration = TargetStateNode.Duration;
                PreviousTime = TargetStateNode.PreviousTime;
                CurrentTime = TargetStateNode.CurrentTime;
                return result;
            }

            // Calculate update range and whether to sync or not
            //-------------------------------------------------------------------------

            SyncTrackTimeRange? syncedRange = null;

            // A supplied range means the parent state machine is being driven via a sync update
            if (updateRange != null)
            {
                syncedRange = updateRange;
            }
            else if (!IsSourceACachedPoseOrOffState && IsSynchronized && blendedSyncTrack != null)
            {
                // Calculate the update range for this frame on the blended sync track
                var percentageTimeDelta = BlendedDuration > 0f ? ctx.DeltaTime / BlendedDuration : 0f;
                var toTime = CurrentTime + percentageTimeDelta;
                syncedRange = new SyncTrackTimeRange(blendedSyncTrack.GetTime(CurrentTime), blendedSyncTrack.GetTime(toTime));
            }

            // Update transition progress
            //-------------------------------------------------------------------------

            // Check if source transition is complete
            if (IsSourceTransition && GetSourceTransitionNode().IsComplete(ctx))
            {
                EndSourceTransition(ctx);
            }

            // With clamping in a synced update the progress advances by the covered sync distance
            if (syncedRange != null && GetOption(TransitionOptions_t.ClampDuration) && blendedSyncTrack != null)
            {
                var eventDistance = blendedSyncTrack.CalculatePercentageCovered(syncedRange.Value);
                TransitionProgress += eventDistance / TransitionDuration;
            }
            else
            {
                TransitionProgress += ctx.DeltaTime / TransitionDuration;
            }

            TransitionProgress = MathUtils.Saturate(TransitionProgress);

            // Calculate blend weight with easing
            CalculateBlendWeight();

            // Update the source state
            //-------------------------------------------------------------------------

            GraphPoseNodeResult sourceNodeResult;
            if (IsSourceACachedPose)
            {
                Debug.Assert(ctx.IsValidCachedPose(cachedPoseBufferID));
                sourceNodeResult = new GraphPoseNodeResult
                {
                    Pose = ctx.GetCachedPoseBuffer(cachedPoseBufferID),
                    RootMotionDelta = Matrix4x4.Identity,
                    SampledEventRange = new(ctx.SampledEvents.Count, ctx.SampledEvents.Count),
                };
            }
            else if (IsSourceAnOffState)
            {
                sourceNodeResult = new GraphPoseNodeResult
                {
                    Pose = null!,
                    RootMotionDelta = Matrix4x4.Identity,
                    SampledEventRange = new(ctx.SampledEvents.Count, ctx.SampledEvents.Count),
                };
            }
            else
            {
                Debug.Assert(SourceNode != null);

                // Set branch state to inactive for source
                var previousBranchState = ctx.BranchState;
                ctx.BranchState = BranchState.Inactive;

                if (syncedRange != null)
                {
                    // The update range is for the target - remove the sync event offset for the source
                    var offset = (int)SyncEventOffset;
                    var syncedRangeValue = syncedRange.Value;
                    var sourceStart = new SyncTrackTime(syncedRangeValue.StartTime.EventIdx - offset, syncedRangeValue.StartTime.PercentageThrough.Value);
                    var sourceEnd = new SyncTrackTime(syncedRangeValue.EndTime.EventIdx - offset, syncedRangeValue.EndTime.PercentageThrough.Value);

                    // Ensure the end time is clamped to the end of the source node
                    if (GetOption(TransitionOptions_t.ClampDuration) && TransitionProgress >= 1f)
                    {
                        sourceEnd = new SyncTrackTime(sourceStart.EventIdx, 1f);
                    }

                    sourceNodeResult = SourceNode.Update(ctx, new SyncTrackTimeRange(sourceStart, sourceEnd));
                }
                else
                {
                    sourceNodeResult = SourceNode.Update(ctx);
                }

                ctx.BranchState = previousBranchState;

                // Cache source node pose
                if (cachedPoseBufferID != -1 && sourceNodeResult.Pose != null)
                {
                    sourceNodeResult.Pose.CopyTo(ctx.GetCachedPoseBuffer(cachedPoseBufferID), 0);
                }
            }

            // Update the target state
            //-------------------------------------------------------------------------

            // Record source layer ctx and reset the layer ctx for the target state
            LayerContext? sourceLayerContext = null;
            if (ctx.IsInLayer)
            {
                sourceLayerContext = ctx.LayerContext;
                targetLayerContext.Reset();
                ctx.LayerContext = targetLayerContext;
            }

            var targetNodeResult = TargetStateNode.Update(ctx, syncedRange);

            if (ctx.IsInLayer && sourceLayerContext != null)
            {
                // Calculate the new layer weights based on the transition progress
                UpdateLayerContext(sourceLayerContext, targetLayerContext);

                // Restore original context
                ctx.LayerContext = sourceLayerContext;
            }

            // Blend poses and root motion
            //-------------------------------------------------------------------------

            if (IsSourceAnOffState)
            {
                // No source pose to blend: the result is the target's (C++ keeps the target task only)
                result.Pose = targetNodeResult.Pose;
                result.RootMotionDelta = targetNodeResult.RootMotionDelta;
            }
            else
            {
                Blender.Blend(
                    sourceNodeResult.Pose,
                    targetNodeResult.Pose,
                    BlendWeight,
                    result.Pose);

                result.RootMotionDelta = Blender.BlendRootMotion(
                    sourceNodeResult.RootMotionDelta,
                    targetNodeResult.RootMotionDelta,
                    BlendWeight,
                    RootMotionBlend);
            }

            result.SampledEventRange = ctx.SampledEvents.BlendEventRanges(sourceNodeResult.SampledEventRange, targetNodeResult.SampledEventRange, BlendWeight);

            // Update internal time and duration
            //-------------------------------------------------------------------------

            if (syncedRange != null && !IsSourceACachedPoseOrOffState && SourceNode != null)
            {
                // Recreate the blended sync track with the new weight
                var sourceSyncTrack = SourceNode.SyncTrack;
                var targetSyncTrack = TargetStateNode.SyncTrack;
                ownedSyncTrack.SetToBlendOf(sourceSyncTrack, targetSyncTrack, BlendWeight);
                blendedSyncTrack = ownedSyncTrack;

                BlendedDuration = SyncTrack.CalculateDurationSynchronized(SourceNode.Duration, TargetStateNode.Duration, sourceSyncTrack.NumEvents, targetSyncTrack.NumEvents, blendedSyncTrack.NumEvents, BlendWeight);
                PreviousTime = blendedSyncTrack.GetPercentageThrough(syncedRange.Value.StartTime);
                CurrentTime = blendedSyncTrack.GetPercentageThrough(syncedRange.Value.EndTime);
            }
            else
            {
                BlendedDuration = MathUtils.Lerp(SourceNode?.Duration ?? 0f, TargetStateNode.Duration, BlendWeight);

                if (BlendedDuration > 0f)
                {
                    var deltaPercentage = ctx.DeltaTime / BlendedDuration;
                    PreviousTime = CurrentTime;
                    CurrentTime = (CurrentTime + deltaPercentage) % 1f;
                }
                else
                {
                    PreviousTime = CurrentTime = 1f;
                }
            }

            // Expose the target duration so any "state completed" conditions trigger correctly
            Duration = TargetStateNode.Duration;

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
            var sourceTransitionTargetState = sourceTransition.TargetStateNode;

            // Shut down the completed source transition (this also releases its cached pose buffer
            // and clears its own source chain), then take over its target state as our source.
            sourceTransition.Shutdown(ctx);
            SourceNode = sourceTransitionTargetState;
            Type = SourceType.State;

            // We need to explicitly set the transition state of the completed transition's target state as 
            // the shutdown of the transition will set it none. This will cause the state machine to potentially
            // transition to that state erroneously!
            GetSourceStateNode().SetTransitioningState(StateNode.TransitionState.TransitioningOut);
        }
    }
}
