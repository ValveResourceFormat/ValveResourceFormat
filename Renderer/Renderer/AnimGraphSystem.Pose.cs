using System.Diagnostics;
using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.AnimLib
{
    /// <summary>Bone transforms of a skeleton, in parent space with cached model space.</summary>
    public class Pose
    {
        /// <summary>What a pose currently holds.</summary>
        public enum PoseType
        {
            /// <summary>Not set.</summary>
            Unset,
            /// <summary>Arbitrary transforms.</summary>
            Pose,
            /// <summary>The skeleton reference pose.</summary>
            ReferencePose,
            /// <summary>The identity additive pose.</summary>
            ZeroPose,
            /// <summary>An additive pose.</summary>
            AdditivePose,
        }

        /// <summary>The skeleton the pose is for.</summary>
        public Skeleton Skeleton { get; private set; }
        readonly FrameBone[] ParentSpaceTransforms = [];
        readonly FrameBone[] ModelSpaceTransforms = [];
        bool CalculatedModelSpace;
        PoseType Type = PoseType.Unset;

        /// <summary>The number of bones.</summary>
        public int NumBones => Skeleton.ParentSpaceReferencePose.Length;

        /// <summary>Creates a pose for <paramref name="skeleton"/> and sets the initial state.</summary>
        public Pose(Skeleton skeleton, PoseType initialState = PoseType.ReferencePose)
        {
            Debug.Assert(skeleton != null);
            Skeleton = skeleton;
            ParentSpaceTransforms = new FrameBone[NumBones];
            ModelSpaceTransforms = new FrameBone[NumBones];
            Reset(initialState);
        }

        /// <summary>Resets to the given state, optionally calculating model space transforms.</summary>
        public void Reset(PoseType initialState, bool calculateModelSpacePose = false)
        {
            switch (initialState)
            {
                case PoseType.ReferencePose: SetToReferencePose(); break;
                case PoseType.ZeroPose: SetToZeroPose(); break;
                default: Type = PoseType.Unset; break;
            }

            CalculatedModelSpace = false;
            if (calculateModelSpacePose)
            {
                CalculateModelSpaceTransforms(NumBones);
            }
        }

        /// <summary>Sets every bone to the reference pose.</summary>
        public void SetToReferencePose()
        {
            Debug.Assert(Skeleton != null);
            Skeleton.ParentSpaceReferencePose.CopyTo(ParentSpaceTransforms, 0);
            Type = PoseType.ReferencePose;
        }

        /// <summary>Sets every bone to the identity additive transform.</summary>
        public void SetToZeroPose()
        {
            Debug.Assert(Skeleton != null);
            Array.Fill(ParentSpaceTransforms, TransformMath.Zero);
            Type = PoseType.ZeroPose;
        }

        /// <summary>Calculate model-space transforms for the requested LOD (number of relevant bones).</summary>
        public void CalculateModelSpaceTransforms(int numRelevantBones)
        {
            Debug.Assert(Skeleton != null);

            var numTotalBones = ParentSpaceTransforms.Length;
            if (numTotalBones == 0)
            {
                return;
            }

            ModelSpaceTransforms[0] = ParentSpaceTransforms[0];
            for (var boneIdx = 1; boneIdx < numRelevantBones; boneIdx++)
            {
                var parentIdx = Skeleton.ParentIndices[boneIdx];
                Debug.Assert(parentIdx < boneIdx);

                // ModelSpace[bone] = ParentSpace[bone] * ModelSpace[parent]
                ModelSpaceTransforms[boneIdx] = ParentSpaceTransforms[boneIdx] * ModelSpaceTransforms[parentIdx];
            }

            CalculatedModelSpace = true;
        }

        /// <summary>Gets the model space transform of a bone, calculating it if not cached.</summary>
        public Transform GetModelSpaceTransform(int boneIdx)
        {
            Debug.Assert(Skeleton != null);
            Debug.Assert(boneIdx < Skeleton.ParentSpaceReferencePose.Length);

            if (CalculatedModelSpace)
            {
                return ModelSpaceTransforms[boneIdx];
            }

            // Otherwise calculate on-demand (matching C++ fallback)
            Span<int> boneParents = stackalloc int[Skeleton.ParentSpaceReferencePose.Length];
            var nextEntry = 0;

            // Get parent list
            var parentIdx = Skeleton.ParentIndices[boneIdx];
            while (parentIdx != -1)
            {
                boneParents[nextEntry++] = parentIdx;
                parentIdx = Skeleton.ParentIndices[parentIdx];
            }

            // Start with bone's parent-space transform
            var boneModelSpaceTransform = ParentSpaceTransforms[boneIdx];

            // If we have parents, accumulate them from root down
            if (nextEntry > 0)
            {
                // Calculate model-space transform of parent
                var arrayIdx = nextEntry - 1;
                parentIdx = boneParents[arrayIdx--];
                var parentModelSpaceTransform = ParentSpaceTransforms[parentIdx];

                for (; arrayIdx >= 0; arrayIdx--)
                {
                    var nextIdx = boneParents[arrayIdx];
                    var nextTransform = ParentSpaceTransforms[nextIdx];
                    parentModelSpaceTransform = nextTransform * parentModelSpaceTransform;
                }

                // Calculate model-space transform of bone
                boneModelSpaceTransform *= parentModelSpaceTransform;
            }

            return boneModelSpaceTransform;
        }

        /// <summary>Gets the parent space transform of a bone.</summary>
        public Transform GetTransform(int boneIdx)
        {
            return ParentSpaceTransforms[boneIdx];
        }

        /// <summary>Sets the parent space transform of a bone.</summary>
        public void SetTransform(int boneIdx, Transform transform)
        {
            Debug.Assert(boneIdx >= 0 && boneIdx < NumBones);
            ParentSpaceTransforms[boneIdx] = transform;
            CalculatedModelSpace = false;
            MarkAsValidPose();
        }

        /// <summary>Copies parent-space transforms in bulk and invalidates cached model-space transforms.</summary>
        public void SetParentSpaceTransforms(ReadOnlySpan<FrameBone> transforms)
        {
            var count = Math.Min(transforms.Length, ParentSpaceTransforms.Length);
            transforms[..count].CopyTo(ParentSpaceTransforms);
            CalculatedModelSpace = false;
            MarkAsValidPose();
        }

        /// <summary>Copies the parent space transforms into a destination span.</summary>
        public void CopyParentSpaceTransformsTo(Span<FrameBone> destination)
        {
            ParentSpaceTransforms.AsSpan(0, Math.Min(destination.Length, ParentSpaceTransforms.Length)).CopyTo(destination);
        }

        void MarkAsValidPose()
        {
            if (Type != PoseType.Pose && Type != PoseType.AdditivePose)
            {
                Type = PoseType.Pose;
            }
        }

        // Helper to compose two transforms (returns a Transform representing a * b)
        private static Transform Compose(in Transform a, in Transform b)
        {
            // Compose by multiplying matrices and decomposing — reuse the existing decomposition logic in Transform
            var ma = a.ToMatrix();
            var mb = b.ToMatrix();
            var combined = ma * mb;
            if (Matrix4x4.Decompose(combined, out var scaleVec, out var rot, out var trans))
            {
                var scale = scaleVec.X;
                return new Transform(trans, scale, Quaternion.Normalize(rot));
            }

            return a;
        }
    }

    // Currently not using tasks and computing poses directly

    /// <summary>The output of a pose node update.</summary>
    public struct GraphPoseNodeResult
    {
#pragma warning disable CA1051 // Do not declare visible instance fields
        /// <summary>The parent space pose.</summary>
        public FrameBone[] Pose;

        /// <summary>The root motion over the update, local to the character.</summary>
        public Transform RootMotionDelta;

        /// <summary>The events the update sampled.</summary>
        public SampledEventRange SampledEventRange;

        /// <summary>
        /// Whether the node had no pose to give, such as a state without a valid child. The pose then holds the
        /// default pose, and layers leave the pose below untouched rather than blending it in.
        /// </summary>
        public bool NoPose;
#pragma warning restore CA1051
    }

    partial class PoseNode
    {
        /// <summary>How many times the node has looped.</summary>
        public int LoopCount { get; protected internal set; }

        /// <summary>The node's duration in seconds.</summary>
        public float Duration { get; protected internal set; }

        /// <summary>Where the node is, as a percentage through its duration.</summary>
        public float CurrentTime { get; protected internal set; }

        /// <summary>Where the node was at the previous update, as a percentage through its duration.</summary>
        public float PreviousTime { get; protected internal set; }

        /// <summary>This node's output pose buffer, in parent (local bone) space.</summary>
        public FrameBone[] PoseTransforms { get; protected internal set; } = [];

        /// <inheritdoc/>
        public override void Instantiate(GraphContext ctx)
        {
            LoopCount = 0;
            Duration = 0f;
            CurrentTime = 0f;
            PreviousTime = 0f;

            // Start from the reference pose so a node that never writes its buffer (not implemented
            // yet, invalid clip) produces the bind pose rather than zero-scale garbage.
            PoseTransforms = new FrameBone[ctx.Graph.ParentSpaceReferencePose.Length];
            ctx.Graph.ParentSpaceReferencePose.CopyTo(PoseTransforms, 0);
        }

        /// <summary>Initializes an animation node with a specific start time.</summary>
        public void Initialize(GraphContext ctx, SyncTrackTime initialTime)
        {
            if (IsInitialized)
            {
                initializationCount++;
            }
            else
            {
                InitializeInternal(ctx, initialTime);
            }
        }

        /// <inheritdoc/>
        public sealed override void Initialize(GraphContext ctx) => Initialize(ctx, default);

        /// <inheritdoc/>
        protected sealed override void InitializeInternal(GraphContext ctx) => InitializeInternal(ctx, default);

        /// <summary>Resets the node state when it becomes active, starting at the given time.</summary>
        protected virtual void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx);

            // Reset node state; nodes are expected to set the duration at initialization time
            LoopCount = 0;
            PreviousTime = 0f;
            CurrentTime = 0f;
            Duration = 0f;
        }

        /// <summary>Whether the node can produce a pose.</summary>
        public virtual bool IsValid => true;

        /// <summary>The sync track for this node's timeline; pass-through nodes forward their child's.</summary>
        public virtual SyncTrack SyncTrack => SyncTrack.Default;

        /// <summary>Advances the node and returns its pose, optionally over a sync track range.</summary>
        public virtual GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            return new GraphPoseNodeResult
            {
                Pose = PoseTransforms,
                RootMotionDelta = Transform.Identity,
                SampledEventRange = new(ctx.SampledEvents.Count, ctx.SampledEvents.Count),
            };
        }
    }

    partial class ReferencePoseNode
    {
        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            PreviousTime = CurrentTime = 1f;
            Duration = 0f;
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx);
            ctx.Graph.ParentSpaceReferencePose.CopyTo(result.Pose, 0);
            return result;
        }
    }

    partial class ZeroPoseNode
    {
        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            PreviousTime = CurrentTime = 1f;
            Duration = 0f;
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx);
            Array.Fill(result.Pose, TransformMath.Zero);
            return result;
        }
    }

    // A pose supplied by game code. Only its root motion is used; the viewer never sets one.
    partial class ExternalPoseNode
    {
        public bool IsPoseSet { get; private set; }

        Transform poseRootMotion = Transform.Identity;
        bool hasRootMotion;
        bool isRootMotionDelta = true;

        public override bool IsValid => IsPoseSet;

        public void SetPoseData(Transform rootMotion, bool isDelta)
        {
            IsPoseSet = true;
            poseRootMotion = rootMotion;
            hasRootMotion = true;
            isRootMotionDelta = isDelta;
        }

        public void ClearPoseData()
        {
            IsPoseSet = false;
            poseRootMotion = Transform.Identity;
            hasRootMotion = false;
            isRootMotionDelta = true;
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);
            PreviousTime = CurrentTime = 1f;
            Duration = 0f;
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            var result = base.Update(ctx);

            if (!IsValid || !hasRootMotion)
            {
                return result;
            }

            result.RootMotionDelta = isRootMotionDelta ? poseRootMotion : poseRootMotion * ctx.WorldTransformInverse;
            return result;
        }
    }

    #region Animation Source Nodes
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
            // C++ warns about a missing animation here; unbound variant slots are routine in CS2
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
            return result;
        }
    }
    #endregion

    // Plays a referenced child graph instance within this graph's layer and branch, reflecting this
    // graph's same-named parameters into the child's control parameters.
    partial class ReferencedGraphNode
    {
        AnimationGraph? childGraph;
        PoseNode? FallbackNode;

        // Child control parameter name and the parent parameter node that drives it
        (string Name, ValueNode Source)[] parameterMapping = [];

        // For a child authored on another skeleton: the child bone for each parent bone by name, -1 if it has none
        int[]? boneMap;

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetOptionalNodeFromIndex(FallbackNodeIdx, ref FallbackNode);

            childGraph = ctx.GetReferencedGraph(ReferencedGraphIdx);

            if (childGraph == null)
            {
                return;
            }

            List<(string, ValueNode)> mapping = [];
            var childNodes = childGraph.Context.Nodes;

            for (var childParamIdx = 0; childParamIdx < childGraph.ParameterNames.Length; childParamIdx++)
            {
                var childParamName = childGraph.ParameterNames[childParamIdx];
                var parentParameterNode = ctx.GetParameterNode(childParamName);
                if (parentParameterNode == null)
                {
                    continue;
                }

                if (GetValueType(parentParameterNode) != GetValueType(childNodes[childParamIdx]))
                {
                    ctx.LogWarning(NodeIdx, $"Mismatch parameter type for referenced graph parameter '{childParamName}'");
                    continue;
                }

                mapping.Add((childParamName, parentParameterNode));
            }

            parameterMapping = [.. mapping];

            // Shared graphs can fall back to a default variation authored on another character's skeleton
            if (!string.Equals(childGraph.SkeletonName, ctx.Graph.SkeletonName, StringComparison.OrdinalIgnoreCase))
            {
                boneMap = BuildBoneMap(ctx.Graph.Skeleton, childGraph.Skeleton);
            }
        }

        static int[] BuildBoneMap(ValveResourceFormat.ResourceTypes.ModelAnimation.Skeleton parent, ValveResourceFormat.ResourceTypes.ModelAnimation.Skeleton child)
        {
            Dictionary<string, int> childBones = new(child.Bones.Length, StringComparer.OrdinalIgnoreCase);

            foreach (var bone in child.Bones)
            {
                childBones.TryAdd(bone.Name, bone.Index);
            }

            var map = new int[parent.Bones.Length];

            for (var i = 0; i < map.Length; i++)
            {
                map[i] = childBones.GetValueOrDefault(parent.Bones[i].Name, -1);
            }

            return map;
        }

        static Type? GetValueType(GraphNode node) => node switch
        {
            BoolValueNode => typeof(BoolValueNode),
            IDValueNode => typeof(IDValueNode),
            FloatValueNode => typeof(FloatValueNode),
            VectorValueNode => typeof(VectorValueNode),
            TargetValueNode => typeof(TargetValueNode),
            _ => null,
        };

        public override bool IsValid => childGraph != null || (FallbackNode?.IsValid ?? false);

        public override SyncTrack SyncTrack
        {
            get
            {
                if (childGraph != null)
                {
                    var childRoot = childGraph.Context.RootNode;
                    return childRoot.IsValid ? childRoot.SyncTrack : SyncTrack.Default;
                }

                return FallbackNode is { IsValid: true } ? FallbackNode.SyncTrack : SyncTrack.Default;
            }
        }

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            if (childGraph != null)
            {
                ReflectControlParametersFromParent(ctx);

                // Reset the referenced instance at the initial time
                childGraph.Context.ResetReferencedGraphState(ctx, initialTime);

                var childRoot = childGraph.Context.RootNode;
                Debug.Assert(childRoot.IsInitialized);
                PreviousTime = childRoot.CurrentTime;
                CurrentTime = childRoot.CurrentTime;
                Duration = childRoot.Duration;
            }
            else
            {
                PreviousTime = CurrentTime = 0f;
                Duration = 0f;

                // Initialize the fallback node if set
                if (FallbackNode != null)
                {
                    FallbackNode.Initialize(ctx, initialTime);
                    Duration = FallbackNode.Duration;
                    PreviousTime = FallbackNode.PreviousTime;
                    CurrentTime = FallbackNode.CurrentTime;
                }
            }
        }

        protected override void ShutdownInternal(GraphContext ctx)
        {
            // The referenced instance itself stays initialized (Esoterica leaves it alive until the
            // owning instance is destroyed); only the fallback participates.
            if (childGraph == null && FallbackNode != null)
            {
                FallbackNode.Shutdown(ctx);
            }

            base.ShutdownInternal(ctx);
        }

        public override GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            if (childGraph == null)
            {
                if (FallbackNode is { IsValid: true })
                {
                    var fallbackResult = FallbackNode.Update(ctx, updateRange);
                    Duration = FallbackNode.Duration;
                    PreviousTime = FallbackNode.PreviousTime;
                    CurrentTime = FallbackNode.CurrentTime;
                    return fallbackResult;
                }

                // A slot the game fills at runtime, empty in the compiled graph
                var emptyResult = base.Update(ctx);
                emptyResult.NoPose = true;
                return emptyResult;
            }

            ReflectControlParametersFromParent(ctx);

            var eventRangeStart = ctx.SampledEvents.Count;
            var childResult = childGraph.Context.EvaluateReferencedGraph(ctx, updateRange);
            ForwardParameterHintsToParent();

            // Surface the child's events so parent conditions can see them
            ctx.SampledEvents.AppendFrom(childGraph.Context.SampledEvents);

            var result = base.Update(ctx);

            if (boneMap == null)
            {
                var count = Math.Min(childResult.Pose.Length, result.Pose.Length);
                childResult.Pose.AsSpan(0, count).CopyTo(result.Pose);
            }
            else
            {
                // Bones the child skeleton lacks keep the pose that leaves them unchanged
                var defaultPose = ctx.GetDefaultPose();
                var count = Math.Min(boneMap.Length, result.Pose.Length);

                for (var i = 0; i < count; i++)
                {
                    var childBone = boneMap[i];
                    result.Pose[i] = childBone >= 0 && childBone < childResult.Pose.Length ? childResult.Pose[childBone] : defaultPose[i];
                }
            }
            result.RootMotionDelta = childResult.RootMotionDelta;
            result.SampledEventRange = new(eventRangeStart, ctx.SampledEvents.Count);
            result.NoPose = childResult.NoPose;

            var childRoot = childGraph.Context.RootNode;
            Duration = childRoot.Duration;
            PreviousTime = childRoot.CurrentTime;
            CurrentTime = childRoot.CurrentTime;

            return result;
        }

        private void ReflectControlParametersFromParent(GraphContext ctx)
        {
            Debug.Assert(childGraph != null);

            foreach (var (name, source) in parameterMapping)
            {
                switch (source)
                {
                    case BoolValueNode boolNode:
                        childGraph.BoolParameters[name] = boolNode.GetValue(ctx);
                        break;
                    case IDValueNode idNode:
                        var id = idNode.GetValue(ctx);
                        childGraph.IdParameters[name] = id.IsValid ? id.Name : string.Empty;
                        break;
                    case FloatValueNode floatNode:
                        childGraph.FloatParameters[name] = floatNode.GetValue(ctx);
                        break;
                    case VectorValueNode vectorNode:
                        childGraph.VectorParameters[name] = new Vector4(vectorNode.GetValue(ctx), 0f);
                        break;
                    case TargetValueNode targetNode:
                        var target = targetNode.GetValue(ctx);
                        childGraph.TargetParameters[name] = target.IsSet ? target.Transform : null;
                        break;
                }
            }
        }

        // Hints reported inside the child graph surface on the parent parameters feeding it
        private void ForwardParameterHintsToParent()
        {
            Debug.Assert(childGraph != null);

            foreach (var (name, source) in parameterMapping)
            {
                if (!childGraph.ParameterHints.TryGetValue(name, out var hint))
                {
                    continue;
                }

                switch (source)
                {
                    case ControlParameterTargetNode targetParameter:
                        targetParameter.ReportHint(hint.Transform, hint.IsWorldSpace);
                        break;
                    case ControlParameterVectorNode vectorParameter:
                        vectorParameter.ReportHint(hint.Transform.Position, hint.IsWorldSpace);
                        break;
                }
            }
        }
    }

    # region Clip Selector Nodes
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

    // Valve extension: selects a clip option by matching an ID parameter against per-option IDs,
    // falling back to a dedicated fallback node when no option matches.
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
    #endregion
}
