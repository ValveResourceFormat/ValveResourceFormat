using System.Diagnostics;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    partial class ClipNode
    {
        public override GraphClip? GetClip(GraphContext ctx) => Clip;
        public override bool IsLooping => AllowLooping;
        public override void DisableRootMotionSampling() => shouldSampleRootMotion = false;
        public override SyncTrack SyncTrack => syncTrackWithOffset ?? Clip?.SyncTrack ?? SyncTrack.Default;

        public GraphClip? Clip;

        public BoolValueNode? ResetTimeValueNode;
        public BoolValueNode? PlayInReverseValueNode;

        public override bool IsValid => Clip != null;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);

            ctx.SetOptionalNodeFromIndex(ResetTimeValueNodeIdx, ref ResetTimeValueNode);
            ctx.SetOptionalNodeFromIndex(PlayInReverseValueNodeIdx, ref PlayInReverseValueNode);

            // DataSlotIdx can be -1 (no clip bound) — leave the node invalid in that case.
            if (DataSlotIdx < 0 || DataSlotIdx >= ctx.Graph.DataSlots.Length)
            {
                Clip = null;
                return;
            }

            Clip = ctx.Graph.DataSlots[DataSlotIdx];

            // Apply the authored start offset to this node's view of the clip's sync track
            syncTrackWithOffset = Clip != null && StartSyncEventOffset != 0
                ? new SyncTrack(Clip.SyncTrack.SyncEvents, StartSyncEventOffset)
                : null;
        }

        SyncTrack? syncTrackWithOffset;

        // Whether the clip currently plays backwards. Time still advances forward; pose and event
        // sampling mirror through (1 - t) (Esoterica AnimationClipNode::CalculateResult).
        bool shouldPlayInReverse;
        bool shouldSampleRootMotion = true;
        bool warnedReverseDuringSync;

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            PlayInReverseValueNode?.Initialize(ctx);

            // Initialize state data
            if (Clip != null)
            {
                // The exposed duration folds in the speed multiplier so parents see scaled time
                Duration = SpeedMultiplier != 0f ? Clip.Duration / SpeedMultiplier : 0f;
                CurrentTime = PreviousTime = SyncTrack.GetPercentageThrough(initialTime);
                Debug.Assert(CurrentTime >= 0f && CurrentTime <= 1f);
            }
            // C++ warns about a missing animation here; unbound variant slots are routine in compiled
            // graphs, so we stay quiet.

            shouldSampleRootMotion = SampleRootMotion;
            shouldPlayInReverse = false;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            PlayInReverseValueNode?.Shutdown(ctx);

            CurrentTime = PreviousTime = 0f;
            base.ShutdownInternal(ctx);
        }

        public override void UpdateSelection(GraphContext ctx)
        {
            //
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx);

            var clip = Clip;
            if (clip == null)
            {
                return result;
            }

            Debug.Assert(CurrentTime >= 0f && CurrentTime <= 1f);

            // Handle single frame animations
            if (clip.FrameCount == 1)
            {
                PreviousTime = 1f;
                CurrentTime = 1f;
                clip.SamplePoseAtFrame(0, result.Pose);
                ctx.RecordClipSample(clip, 0f);
                SampleAnimationEvents(ctx, ref result);
                return result;
            }

            // Synchronized Update
            if (updateRange != null)
            {
                // The reverse toggle is not processed during a synced update (the sync range drives
                // time), but an already-latched reversal still mirrors the sampling below.
                if ((PlayInReverseValueNode != null || shouldPlayInReverse) && !warnedReverseDuringSync)
                {
                    warnedReverseDuringSync = true;
                    ctx.LogWarning(NodeIdx, "'Play reversed' has no effect when used with time synchronization!");
                }

                PreviousTime = SyncTrack.GetPercentageThrough(updateRange.Value.StartTime);
                CurrentTime = SyncTrack.GetPercentageThrough(updateRange.Value.EndTime);
                LoopCount = 0;

                return CalculateResult(ctx, clip, result);
            }

            // Unsynchronized Update

            // Should we change the playback direction? Mirror the current time so the pose is
            // continuous across the toggle.
            if (PlayInReverseValueNode != null && shouldPlayInReverse != PlayInReverseValueNode.GetValue(ctx))
            {
                shouldPlayInReverse = !shouldPlayInReverse;
                CurrentTime = 1f - CurrentTime;
                if (CurrentTime == 1f)
                {
                    CurrentTime = 0f;
                }
            }

            var resetTime = ResetTimeValueNode?.GetValue(ctx) ?? false;
            if (resetTime)
            {
                CurrentTime = 0f;
                PreviousTime = 0f;
            }

            var deltaPercentage = Duration > 0f ? ctx.DeltaTime / Duration : 0f;

            PreviousTime = CurrentTime;
            CurrentTime += deltaPercentage;

            if (IsLooping || ctx.Graph.ForceLoopingClips)
            {
                if (CurrentTime > 1f)
                {
                    var loops = (int)CurrentTime;
                    LoopCount += loops;
                    CurrentTime -= loops;

                    Debug.Assert(CurrentTime >= 0f && CurrentTime <= 1f);
                }
            }
            else
            {
                CurrentTime = MathUtils.Saturate(CurrentTime);
            }

            return CalculateResult(ctx, clip, result);
        }

        private GraphPoseNodeResult CalculateResult(GraphContext ctx, GraphClip clip, GraphPoseNodeResult result)
        {
            var snapMode = SampleAnimationEvents(ctx, ref result);

            if (shouldSampleRootMotion)
            {
                // Reversed playback samples the mirrored range, computed here rather than in the
                // clip's delta query to keep that free of the rare special case
                if (shouldPlayInReverse)
                {
                    var sampleStartTime = 1f - PreviousTime;
                    var sampleEndTime = 1f - CurrentTime;

                    if (PreviousTime <= CurrentTime)
                    {
                        result.RootMotionDelta = clip.GetRootMotionDeltaNoLooping(sampleStartTime, sampleEndTime);
                    }
                    else
                    {
                        var preLoopDelta = clip.GetRootMotionDeltaNoLooping(sampleStartTime, 0f);
                        var postLoopDelta = clip.GetRootMotionDeltaNoLooping(1f, sampleEndTime);
                        result.RootMotionDelta = postLoopDelta * preLoopDelta;
                    }
                }
                else
                {
                    result.RootMotionDelta = clip.GetRootMotionDelta(PreviousTime, CurrentTime);
                }
            }

            var sampleTime = shouldPlayInReverse ? 1f - CurrentTime : CurrentTime;

            if (snapMode is { } frameSelectionMode)
            {
                var frameTime = clip.GetFrameTime(sampleTime);
                var frameIndex = frameSelectionMode == FrameSnapEventMode.Round ? frameTime.NearestFrameIndex : frameTime.LowerBoundFrameIndex;
                sampleTime = clip.GetPercentageThrough(frameIndex);
            }

            clip.SamplePoseAtPercentage(sampleTime, result.Pose);
            ctx.RecordClipSample(clip, sampleTime);
            return result;
        }

        /// <summary>
        /// Samples the clip's events for the time range covered this update into the graph's event
        /// buffer: duration events that are active at the current time, and instant events that were
        /// crossed between the previous and current time (accounting for looping).
        /// Returns the frame selection mode of a sampled snap to frame event, if any.
        /// </summary>
        private FrameSnapEventMode? SampleAnimationEvents(GraphContext ctx, ref GraphPoseNodeResult result)
        {
            var clip = Clip;
            Debug.Assert(clip != null);

            var isFromActiveBranch = ctx.BranchState == BranchState.Active;
            var startCount = ctx.SampledEvents.Count;

            // Reversed playback covers the mirrored clip range: invert the times and swap the
            // start and end (Esoterica AnimationClipNode::CalculateResult).
            var from = shouldPlayInReverse ? 1f - CurrentTime : PreviousTime;
            var to = shouldPlayInReverse ? 1f - PreviousTime : CurrentTime;

            // Duration events report how far through they are at the clip time reached this
            // update (Esoterica uses the single sample end time, also for looped ranges),
            // mirrored back for reversed playback.
            var sampleEndTime = shouldPlayInReverse ? 1f - CurrentTime : CurrentTime;

            if (to >= from)
            {
                SampleEventsInRange(ctx, NodeIdx, clip, from, to, to >= 1f, sampleEndTime, shouldPlayInReverse, isFromActiveBranch);
            }
            else // Looped this update
            {
                SampleEventsInRange(ctx, NodeIdx, clip, from, 1f, true, sampleEndTime, shouldPlayInReverse, isFromActiveBranch);
                SampleEventsInRange(ctx, NodeIdx, clip, 0f, to, false, sampleEndTime, shouldPlayInReverse, isFromActiveBranch);
            }

            // Emit this clip node's authored graph events every update (Generic type)
            foreach (var graphEventID in GraphEvents)
            {
                ctx.SampledEvents.EmplaceGraphEvent(NodeIdx, GraphEventType.Generic, graphEventID, isFromActiveBranch);
            }

            result.SampledEventRange = new(startCount, ctx.SampledEvents.Count);

            // The last snap to frame event decides
            FrameSnapEventMode? snapMode = null;
            for (var i = startCount; i < ctx.SampledEvents.Count; i++)
            {
                if (TryGetFrameSnapMode(ctx.SampledEvents[i], out var mode))
                {
                    snapMode = mode;
                }
            }

            return snapMode;
        }

        public static bool TryGetFrameSnapMode(in SampledEvent sampledEvent, out FrameSnapEventMode mode)
        {
            mode = FrameSnapEventMode.Floor;

            if (sampledEvent.AnimEvent?.ClassName != "CNmFrameSnapEvent")
            {
                return false;
            }

            if (Enum.TryParse<FrameSnapEventMode>(sampledEvent.AnimEvent.Data.GetStringProperty("m_frameSnapMode", string.Empty), out var parsed))
            {
                mode = parsed;
            }

            return true;
        }

        /// <summary>
        /// Samples every clip event whose time range overlaps [rangeFrom, rangeTo), with the trailing
        /// edge included at the very end of the clip (Esoterica AnimationClip::GetEventsForRange).
        /// </summary>
        public static void SampleEventsInRange(GraphContext ctx, short nodeIdx, GraphClip clip, float rangeFrom, float rangeTo, bool includeEnd, float sampleEndTime, bool playInReverse, bool isFromActiveBranch)
        {
            var events = clip.Animation.Events;
            var clipDuration = clip.Animation.Duration;

            if (events.Length == 0 || clipDuration <= 0f)
            {
                return;
            }

            foreach (var clipEvent in events)
            {
                var eventStart = clipEvent.StartCycle;
                var eventEnd = eventStart + (clipEvent.Duration / clipDuration);

                var overlaps = clipEvent.Duration > 0f
                    ? eventStart < rangeTo && eventEnd > rangeFrom
                    : eventStart >= rangeFrom && (eventStart < rangeTo || (includeEnd && eventStart <= rangeTo && rangeTo >= 1f));

                if (clipEvent.Duration > 0f && includeEnd && rangeTo >= 1f && eventEnd >= 1f && eventStart < 1f)
                {
                    overlaps = overlaps || eventStart < rangeTo;
                }

                if (!overlaps)
                {
                    continue;
                }

                var percentageThrough = 1f;
                if (clipEvent.Duration > 0f)
                {
                    percentageThrough = MathUtils.Saturate((sampleEndTime - eventStart) / (eventEnd - eventStart));
                    if (playInReverse)
                    {
                        percentageThrough = 1f - percentageThrough;
                    }
                }

                ctx.SampledEvents.EmplaceAnimationEvent(nodeIdx, clipEvent, percentageThrough, isFromActiveBranch);
            }
        }
    }

    // Plays a clip at a time set directly by a value node rather than advancing it.
    partial class TimeControlledClipNode
    {
        public GraphClip? Clip;
        FloatValueNode? TimeValueNode;
        BoolValueNode? PlayInReverseValueNode;

        bool shouldPlayInReverse;
        bool isFirstUpdate;
        bool hasLooped;
        bool warnedSync;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetOptionalNodeFromIndex(TimeValueNodeIdx, ref TimeValueNode);
            ctx.SetOptionalNodeFromIndex(PlayInReverseValueNodeIdx, ref PlayInReverseValueNode);

            Clip = DataSlotIdx >= 0 && DataSlotIdx < ctx.Graph.DataSlots.Length ? ctx.Graph.DataSlots[DataSlotIdx] : null;
        }

        public override bool IsValid => Clip != null;

        public override SyncTrack SyncTrack => Clip?.SyncTrack ?? SyncTrack.Default;

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            TimeValueNode?.Initialize(ctx);
            PlayInReverseValueNode?.Initialize(ctx);

            if (Clip != null)
            {
                Duration = Clip.Duration;
            }

            // Set the initial time from the parameters
            SetCurrentTimeFromParameters(ctx);
            PreviousTime = CurrentTime;

            shouldPlayInReverse = false;
            isFirstUpdate = true;
            hasLooped = false;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            PlayInReverseValueNode?.Shutdown(ctx);
            TimeValueNode?.Shutdown(ctx);

            CurrentTime = PreviousTime = 0f;
            base.ShutdownInternal(ctx);
        }

        void SetCurrentTimeFromParameters(GraphContext ctx)
        {
            var value = TimeValueNode?.GetValue(ctx) ?? 0f;

            // Normalized time, where a whole number of loops reads as the end of the clip
            var loopCount = MathF.Truncate(value);
            CurrentTime = value - loopCount;
            if (loopCount > 0f && CurrentTime == 0f)
            {
                CurrentTime = 1f;
            }

            // Handle negative input values
            if (CurrentTime < 0f)
            {
                CurrentTime = 1f - MathF.Abs(CurrentTime);
            }

            Debug.Assert(CurrentTime >= 0f && CurrentTime <= 1f);

            // Invert input time if we are in-reverse
            if (shouldPlayInReverse)
            {
                CurrentTime = 1f - CurrentTime;
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (Clip == null)
            {
                return base.Update(ctx);
            }

            hasLooped = false;

            if (updateRange != null && !warnedSync)
            {
                warnedSync = true;
                ctx.LogWarning(NodeIdx, "Time controlled nodes ignore synchronization!");
            }

            // Should we change the playback direction?
            if (PlayInReverseValueNode != null && shouldPlayInReverse != PlayInReverseValueNode.GetValue(ctx))
            {
                shouldPlayInReverse = !shouldPlayInReverse;
                CurrentTime = 1f - CurrentTime;

                if (CurrentTime == 1f)
                {
                    CurrentTime = 0f;
                }
            }

            if (Clip.FrameCount == 1)
            {
                PreviousTime = isFirstUpdate ? 0f : 1f;
                CurrentTime = 1f;
            }
            else
            {
                PreviousTime = CurrentTime;
                SetCurrentTimeFromParameters(ctx);

                // Check for looping
                if (CurrentTime < PreviousTime)
                {
                    hasLooped = true;
                }
            }

            return CalculateResult(ctx, Clip);
        }

        GraphPoseNodeResult CalculateResult(GraphContext ctx, GraphClip clip)
        {
            var result = base.Update(ctx);

            var actualSampleStartTime = PreviousTime;
            var actualSampleEndTime = CurrentTime;

            // Invert times and swap the start and end times to create the correct sampling range for events
            if (shouldPlayInReverse)
            {
                actualSampleEndTime = 1f - CurrentTime;
                actualSampleStartTime = 1f - PreviousTime;
            }

            // Events
            var startCount = ctx.SampledEvents.Count;
            var isFromActiveBranch = ctx.BranchState == BranchState.Active;

            var eventSampleStartTime = shouldPlayInReverse ? actualSampleEndTime : actualSampleStartTime;
            var eventSampleEndTime = shouldPlayInReverse ? actualSampleStartTime : actualSampleEndTime;
            var sampleEndTime = shouldPlayInReverse ? 1f - CurrentTime : CurrentTime;

            if (hasLooped)
            {
                ClipNode.SampleEventsInRange(ctx, NodeIdx, clip, eventSampleStartTime, 1f, true, sampleEndTime, shouldPlayInReverse, isFromActiveBranch);
                ClipNode.SampleEventsInRange(ctx, NodeIdx, clip, 0f, eventSampleEndTime, eventSampleEndTime >= 1f, sampleEndTime, shouldPlayInReverse, isFromActiveBranch);

                // Long duration events can be sampled by both ranges, drop the duplicates
                for (var i = ctx.SampledEvents.Count - 1; i >= startCount; i--)
                {
                    for (var j = startCount; j < i; j++)
                    {
                        if (IsSameSampledEvent(ctx.SampledEvents[i], ctx.SampledEvents[j]))
                        {
                            ctx.SampledEvents.RemoveAt(j);
                            break;
                        }
                    }
                }
            }
            else
            {
                ClipNode.SampleEventsInRange(ctx, NodeIdx, clip, eventSampleStartTime, eventSampleEndTime, eventSampleEndTime >= 1f, sampleEndTime, shouldPlayInReverse, isFromActiveBranch);
            }

            foreach (var graphEventID in GraphEvents)
            {
                ctx.SampledEvents.EmplaceGraphEvent(NodeIdx, GraphEventType.Generic, graphEventID, isFromActiveBranch);
            }

            result.SampledEventRange = new(startCount, ctx.SampledEvents.Count);

            // The first snap to frame event decides
            FrameSnapEventMode? snapMode = null;
            for (var i = startCount; i < ctx.SampledEvents.Count; i++)
            {
                if (ClipNode.TryGetFrameSnapMode(ctx.SampledEvents[i], out var mode))
                {
                    snapMode = mode;
                    break;
                }
            }

            // Root motion
            if (SampleRootMotion)
            {
                if (shouldPlayInReverse)
                {
                    if (PreviousTime <= CurrentTime)
                    {
                        result.RootMotionDelta = clip.GetRootMotionDeltaNoLooping(actualSampleStartTime, actualSampleEndTime);
                    }
                    else
                    {
                        var preLoopDelta = clip.GetRootMotionDeltaNoLooping(actualSampleStartTime, 0f);
                        var postLoopDelta = clip.GetRootMotionDeltaNoLooping(1f, actualSampleEndTime);
                        result.RootMotionDelta = postLoopDelta * preLoopDelta;
                    }
                }
                else
                {
                    result.RootMotionDelta = clip.GetRootMotionDelta(PreviousTime, CurrentTime);
                }
            }

            // Pose
            var sampleTime = shouldPlayInReverse ? 1f - CurrentTime : CurrentTime;

            if (snapMode is { } frameSelectionMode)
            {
                var frameTime = clip.GetFrameTime(sampleTime);
                var frameIndex = frameSelectionMode == FrameSnapEventMode.Round ? frameTime.NearestFrameIndex : frameTime.LowerBoundFrameIndex;
                sampleTime = clip.GetPercentageThrough(frameIndex);
            }

            clip.SamplePoseAtPercentage(sampleTime, result.Pose);
            ctx.RecordClipSample(clip, sampleTime);

            isFirstUpdate = false;
            return result;
        }

        static bool IsSameSampledEvent(in SampledEvent a, in SampledEvent b)
            => a.SourceNodeIdx == b.SourceNodeIdx
            && a.IsGraphEvent == b.IsGraphEvent
            && a.IsFromActiveBranch == b.IsFromActiveBranch
            && a.IsIgnored == b.IsIgnored
            && a.GraphEventType == b.GraphEventType
            && a.Weight == b.Weight
            && a.PercentageThrough == b.PercentageThrough
            && a.ID == b.ID
            && ReferenceEquals(a.AnimEvent, b.AnimEvent);
    }

    partial class AnimationPoseNode
    {
        public FloatValueNode? PoseTimeValueNode;
        public GraphClip? Clip;

        public override bool IsValid => Clip != null;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetOptionalNodeFromIndex(PoseTimeValueNodeIdx, ref PoseTimeValueNode);

            // DataSlotIdx can be -1 (no clip bound) — leave the node invalid in that case.
            if (DataSlotIdx < 0 || DataSlotIdx >= ctx.Graph.DataSlots.Length)
            {
                Clip = null;
                return;
            }

            Clip = ctx.Graph.DataSlots[DataSlotIdx];
            // set to null if skeletons don't match
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            PoseTimeValueNode?.Initialize(ctx);

            PreviousTime = CurrentTime = 1f;
            Duration = 0f;
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            PoseTimeValueNode?.Shutdown(ctx);
            base.ShutdownInternal(ctx);
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx);

            var clip = Clip;
            if (clip == null)
            {
                return result;
            }

            if (clip.FrameCount == 1)
            {
                clip.SamplePoseAtFrame(0, result.Pose);
                ctx.RecordClipSample(clip, 0f);
                return result;
            }

            var timeValue = PoseTimeValueNode?.GetValue(ctx) ?? UserSpecifiedTime;

            // Optional remap
            if (InputTimeRemapRange.IsSet)
            {
                timeValue = InputTimeRemapRange.GetPercentageThroughClamped(timeValue);
            }

            // Convert to percentage
            if (UseFramesAsInput)
            {
                timeValue /= clip.FrameCount - 1;
            }

            CurrentTime = MathUtils.Saturate(timeValue);
            PreviousTime = CurrentTime;

            clip.SamplePoseAtPercentage(CurrentTime, result.Pose);
            ctx.RecordClipSample(clip, CurrentTime);
            return result;
        }
    }

    // An interface to directly access a selected animation
    // This is needed to ensure certain animation nodes only operate on animations directly
    abstract partial class ClipReferenceNode
    {
        public virtual GraphClip? GetClip(GraphContext ctx) => SelectedOption?.GetClip(ctx);
        public virtual bool IsLooping => SelectedOption?.IsLooping ?? false;
        public virtual void DisableRootMotionSampling() => SelectedOption?.DisableRootMotionSampling();
        public ClipReferenceNode? SelectedOption;

        public abstract void UpdateSelection(GraphContext ctx);

        /// <summary>
        /// Selects an option and initializes it; an invalid selection is shut down and discarded
        /// (Esoterica AnimationClipSelectorNode::InitializeInternal). Called by the concrete
        /// selector nodes from their initialization — a plain ClipNode does not select.
        /// </summary>
        protected void InitializeSelection(GraphContext ctx, SyncTrackTime initialTime)
        {
            UpdateSelection(ctx);

            if (SelectedOption != null)
            {
                SelectedOption.Initialize(ctx, initialTime);

                if (SelectedOption.IsValid)
                {
                    Duration = SelectedOption.Duration;
                    PreviousTime = SelectedOption.PreviousTime;
                    CurrentTime = SelectedOption.CurrentTime;
                }
                else
                {
                    SelectedOption.Shutdown(ctx);
                    SelectedOption = null;
                }
            }

            if (SelectedOption == null)
            {
                ctx.LogWarning(NodeIdx, "Clip Selector: Failed to select a valid option!");
            }
        }

        protected void ShutdownSelection(GraphContext ctx)
        {
            if (SelectedOption != null)
            {
                SelectedOption.Shutdown(ctx);
                SelectedOption = null;
            }
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (SelectedOption != null)
            {
                var result = SelectedOption.Update(ctx, updateRange);
                Duration = SelectedOption.Duration;
                PreviousTime = SelectedOption.PreviousTime;
                CurrentTime = SelectedOption.CurrentTime;
                return result;
            }

            return base.Update(ctx);
        }
    }

    partial class ClipSelectorNode
    {
        public ClipReferenceNode[] OptionNodes;
        public BoolValueNode[] ConditionNodes;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref ConditionNodes);
        }

        // Note: condition nodes are not part of the selector lifecycle upstream; they are read
        // transiently during selection.
        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            InitializeSelection(ctx, initialTime);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            ShutdownSelection(ctx);
            base.ShutdownInternal(ctx);
        }

        public int PickOption(GraphContext ctx)
        {
            for (var i = 0; i < ConditionNodes.Length; i++)
            {
                var conditionPassed = ConditionNodes[i].GetValue(ctx);
                if (conditionPassed)
                {
                    return i;
                }
            }

            return -1;
        }

        public override void UpdateSelection(GraphContext ctx)
        {
            var selectedIndex = PickOption(ctx);
            if (selectedIndex >= 0 && selectedIndex < OptionNodes.Length)
            {
                SelectedOption = OptionNodes[selectedIndex];
            }
            else
            {
                SelectedOption = null;
            }
        }
    }

    // Selects a clip option by matching an ID parameter against per-option IDs, falling back to a
    // dedicated fallback node when no option matches.
    partial class IDBasedClipSelectorNode
    {
        public ClipReferenceNode[] OptionNodes;
        public IDValueNode ParameterNode;
        public ClipReferenceNode? FallbackNode;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodeFromIndex(ParameterNodeIdx, ref ParameterNode);
            ctx.SetOptionalNodeFromIndex(FallbackNodeIdx, ref FallbackNode);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            InitializeSelection(ctx, initialTime);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            ShutdownSelection(ctx);
            base.ShutdownInternal(ctx);
        }

        public override void UpdateSelection(GraphContext ctx)
        {
            SelectedOption = FallbackNode;

            var id = ParameterNode.GetValue(ctx);
            var optionCount = Math.Min(OptionIDs.Length, OptionNodes.Length);

            for (var i = 0; i < optionCount; i++)
            {
                if (OptionIDs[i] == id)
                {
                    if (IgnoreInvalidOptions && !OptionNodes[i].IsValid)
                    {
                        continue;
                    }

                    SelectedOption = OptionNodes[i];
                    break;
                }
            }
        }
    }

    // Selects the clip option whose root motion ends closest to a target, in place and in facing.
    partial class TargetSelectorNode
    {
        struct Option
        {
            public int OptionIdx;
            public Vector3 EndPoint;
            public Quaternion EndOrientation;
            public float OrientationScore;
            public float PositionScore;
        }

        ClipReferenceNode[] OptionNodes;
        TargetValueNode ParameterNode;
        Transform targetTransform;
        SyncTrackTime selectionTime;
        readonly List<Option> selectionOptions = [];

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodeFromIndex(ParameterNodeIdx, ref ParameterNode);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            selectionTime = initialTime;
            InitializeSelection(ctx, initialTime);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            ShutdownSelection(ctx);
            base.ShutdownInternal(ctx);
        }

        public override void UpdateSelection(GraphContext ctx)
        {
            SelectedOption = null;

            var target = ParameterNode.GetValue(ctx);
            if (!target.IsSet || target.IsBoneTarget)
            {
                ctx.LogWarning(NodeIdx, "Invalid target provided to target selector!");
                return;
            }

            targetTransform = target.Transform;
            if (IsWorldSpaceTarget)
            {
                targetTransform *= ctx.WorldTransformInverse;
            }

            var selectedIdx = SelectOption(ctx, selectionTime);
            if (selectedIdx != -1)
            {
                SelectedOption = OptionNodes[selectedIdx];
            }
        }

        int SelectOption(GraphContext ctx, SyncTrackTime initialTime)
        {
            Debug.Assert(OrientationScoreWeight + PositionScoreWeight != 0f);

            // Generate the set of options
            selectionOptions.Clear();
            var maxDistance = 0f;

            var hasInitialTime = initialTime.EventIdx != 0 || MathF.Abs(initialTime.PercentageThrough.Value) > TransformMath.Epsilon;

            for (var i = 0; i < OptionNodes.Length; i++)
            {
                var clip = OptionNodes[i].IsValid ? OptionNodes[i].GetClip(ctx) : null;
                if (clip != null && clip.RootMotion.IsValid)
                {
                    var endTransform = hasInitialTime
                        ? clip.RootMotion.GetDelta(clip.SyncTrack.GetPercentageThrough(initialTime), 1f)
                        : clip.RootMotion.TotalDelta;

                    selectionOptions.Add(new Option
                    {
                        OptionIdx = i,
                        EndOrientation = endTransform.Angle,
                        EndPoint = endTransform.Position,
                    });

                    maxDistance = MathF.Max(maxDistance, endTransform.Position.Length());
                }
                else if (!IgnoreInvalidOptions)
                {
                    ctx.LogWarning(NodeIdx, "Invalid input detected for target selector");
                    return -1;
                }
            }

            // Scoring
            var options = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(selectionOptions);

            foreach (ref var option in options)
            {
                if (OrientationScoreWeight > 0f)
                {
                    var angle = MathUtils.AngleBetween(option.EndOrientation, targetTransform.Angle);

                    option.OrientationScore = (1f - MathF.Abs(angle / MathF.PI)) * OrientationScoreWeight;
                }

                if (PositionScoreWeight > 0f && maxDistance > TransformMath.Epsilon)
                {
                    var distance = Vector3.Distance(option.EndPoint, targetTransform.Position);
                    option.PositionScore = (1f - (distance / maxDistance)) * PositionScoreWeight;
                }
            }

            // Pick the highest score
            var selectedIdx = -1;
            var highestScore = float.MinValue;

            foreach (ref readonly var option in options)
            {
                var finalScore = option.OrientationScore + option.PositionScore;
                if (finalScore > highestScore)
                {
                    selectedIdx = option.OptionIdx;
                    highestScore = finalScore;
                }
            }

            return selectedIdx;
        }
    }

    partial class ParameterizedClipSelectorNode
    {
        public ClipReferenceNode[] OptionNodes;
        public FloatValueNode ParameterNode;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodeFromIndex(ParameterNodeIdx, ref ParameterNode);
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            InitializeSelection(ctx, initialTime);
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            ShutdownSelection(ctx);
            base.ShutdownInternal(ctx);
        }

        public int PickOption(GraphContext ctx)
            => ParameterizedOptions.Pick(ctx, OptionNodes, OptionWeights, HasWeightsSet, IgnoreInvalidOptions, ParameterNode.GetValue(ctx));

        public override void UpdateSelection(GraphContext ctx)
        {
            var selectedIndex = PickOption(ctx);
            if (selectedIndex >= 0 && selectedIndex < OptionNodes.Length)
            {
                SelectedOption = OptionNodes[selectedIndex];
            }
            else
            {
                SelectedOption = null;
            }
        }
    }
}
