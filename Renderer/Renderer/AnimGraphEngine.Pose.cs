using System.Diagnostics;
using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer.AnimLib
{
    class Pose
    {
        public enum PoseType
        {
            Unset,
            Pose,
            ReferencePose,
            ZeroPose,
            AdditivePose,
        }

        public Skeleton Skeleton { get; private set; }
        readonly FrameBone[] ParentSpaceTransforms = [];
        readonly FrameBone[] ModelSpaceTransforms = [];
        bool CalculatedModelSpace;
        PoseType Type = PoseType.Unset;

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

        public void SetToReferencePose()
        {
            Debug.Assert(Skeleton != null);
            Skeleton.ParentSpaceReferencePose.CopyTo(ParentSpaceTransforms, 0);
            Type = PoseType.ReferencePose;
        }

        public void SetToZeroPose()
        {
            Debug.Assert(Skeleton != null);
            Array.Fill(ParentSpaceTransforms, FrameBone.Identity);
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

        public Transform GetTransform(int boneIdx)
        {
            return ParentSpaceTransforms[boneIdx];
        }

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

    struct GraphPoseNodeResult
    {
        public FrameBone[] Pose;
        public Matrix4x4 RootMotionDelta;
        public SampledEventRange SampledEventRange;
    }

    partial class PoseNode
    {
        public int LoopCount;
        public float Duration;   /* Seconds */
        public float CurrentTime; /* Percent */
        public float PreviousTime;  /* Percent */

        /// <summary>This node's output pose buffer, in parent (local bone) space.</summary>
        public FrameBone[] PoseTransforms = [];

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

        public sealed override void Initialize(GraphContext ctx) => Initialize(ctx, default);

        protected sealed override void InitializeInternal(GraphContext ctx) => InitializeInternal(ctx, default);

        protected virtual void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx);

            // Reset node state; nodes are expected to set the duration at initialization time
            LoopCount = 0;
            PreviousTime = 0f;
            CurrentTime = 0f;
            Duration = 0f;
        }

        public virtual bool IsValid => true;

        /// <summary>The sync track for this node's timeline; pass-through nodes forward their child's.</summary>
        public virtual SyncTrack SyncTrack => SyncTrack.Default;

        public virtual GraphPoseNodeResult Update(GraphContext ctx, SyncTrackTimeRange? updateRange = null)
        {
            return new GraphPoseNodeResult
            {
                Pose = PoseTransforms,
                RootMotionDelta = Matrix4x4.Identity,
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
            for (var i = 0; i < result.Pose.Length; i++)
            {
                result.Pose[i] = FrameBone.Identity;
            }
            return result;
        }
    }

    #region Animation Source Nodes
    partial class ClipNode
    {
        public override GraphClip? GetClip(GraphContext ctx) => Clip;
        public override bool IsLooping => AllowLooping;
        public override bool DisableRootMotionSampling => !SampleRootMotion;
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

                clip.SamplePoseAtPercentage(shouldPlayInReverse ? 1f - CurrentTime : CurrentTime, result.Pose);
                SampleAnimationEvents(ctx, ref result);
                return result;
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

            // sample animation pose at current time
            var frame = clip.SamplePoseAtPercentage(shouldPlayInReverse ? 1f - CurrentTime : CurrentTime, result.Pose);

            // root motion
            // frame.Movement.Position;

            SampleAnimationEvents(ctx, ref result);
            return result;
        }

        /// <summary>
        /// Samples the clip's events for the time range covered this update into the graph's event
        /// buffer: duration events that are active at the current time, and instant events that were
        /// crossed between the previous and current time (accounting for looping).
        /// </summary>
        private void SampleAnimationEvents(GraphContext ctx, ref GraphPoseNodeResult result)
        {
            var clip = Clip;
            Debug.Assert(clip != null);

            var isFromActiveBranch = ctx.BranchState == BranchState.Active;
            var startCount = ctx.SampledEvents.Count;

            var events = clip.Animation.Events;
            var clipDuration = clip.Animation.Duration;

            if (events.Length > 0 && clipDuration > 0f)
            {
                // Reversed playback covers the mirrored clip range: invert the times and swap the
                // start and end (Esoterica AnimationClipNode::CalculateResult).
                var from = shouldPlayInReverse ? 1f - CurrentTime : PreviousTime;
                var to = shouldPlayInReverse ? 1f - PreviousTime : CurrentTime;

                // Duration events report how far through they are at the clip time reached this
                // update (Esoterica uses the single sample end time, also for looped ranges),
                // mirrored back for reversed playback.
                var sampleEndTime = shouldPlayInReverse ? 1f - CurrentTime : CurrentTime;

                // Every event whose time range overlaps [from, to) is sampled, with the trailing edge
                // included at the very end of the clip (Esoterica AnimationClip::GetEventsForRange).
                // A wrapped range (looping) samples [from, 1) and [0, to).
                void SampleRange(float rangeFrom, float rangeTo, bool includeEnd)
                {
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
                            if (shouldPlayInReverse)
                            {
                                percentageThrough = 1f - percentageThrough;
                            }
                        }

                        ctx.SampledEvents.EmplaceAnimationEvent(NodeIdx, clipEvent, percentageThrough, isFromActiveBranch);
                    }
                }

                if (to >= from)
                {
                    SampleRange(from, to, to >= 1f);
                }
                else // Looped this update
                {
                    SampleRange(from, 1f, true);
                    SampleRange(0f, to, false);
                }
            }

            // Emit this clip node's authored graph events every update (Generic type)
            foreach (var graphEventID in GraphEvents)
            {
                ctx.SampledEvents.EmplaceGraphEvent(NodeIdx, GraphEventType.Generic, graphEventID, isFromActiveBranch);
            }

            result.SampledEventRange = new(startCount, ctx.SampledEvents.Count);
        }
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


    // Plays a referenced child graph (its own instance with its own context), pushing the parent's
    // same-named control parameters down every update and surfacing the child's sampled events.
    partial class ReferencedGraphNode
    {
        AnimationGraph? childGraph;
        PoseNode? FallbackNode;

        string[] sharedBoolParameters = [];
        string[] sharedFloatParameters = [];
        string[] sharedIdParameters = [];
        string[] sharedVectorParameters = [];
        string[] sharedTargetParameters = [];

        public override void Instantiate(GraphContext ctx)
        {
            base.Instantiate(ctx);
            ctx.SetOptionalNodeFromIndex(FallbackNodeIdx, ref FallbackNode);

            childGraph = ctx.GetReferencedGraph(ReferencedGraphIdx);

            if (childGraph != null)
            {
                var parent = ctx.Graph;
                sharedBoolParameters = [.. childGraph.BoolParameters.Keys.Where(parent.BoolParameters.ContainsKey)];
                sharedFloatParameters = [.. childGraph.FloatParameters.Keys.Where(parent.FloatParameters.ContainsKey)];
                sharedIdParameters = [.. childGraph.IdParameters.Keys.Where(parent.IdParameters.ContainsKey)];
                sharedVectorParameters = [.. childGraph.VectorParameters.Keys.Where(parent.VectorParameters.ContainsKey)];
                sharedTargetParameters = [.. childGraph.TargetParameters.Keys.Where(parent.TargetParameters.ContainsKey)];
            }
        }

        public override bool IsValid => childGraph != null || (FallbackNode?.IsValid ?? false);

        public override SyncTrack SyncTrack => childGraph?.Context.RootNode.SyncTrack
            ?? FallbackNode?.SyncTrack
            ?? SyncTrack.Default;

        protected override void InitializeInternal(GraphContext ctx, SyncTrackTime initialTime)
        {
            base.InitializeInternal(ctx, initialTime);

            if (childGraph != null)
            {
                // Reset the referenced instance at the initial time
                childGraph.ResetGraphState(initialTime);

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
                if (FallbackNode != null)
                {
                    var fallbackResult = FallbackNode.Update(ctx);
                    Duration = FallbackNode.Duration;
                    PreviousTime = FallbackNode.PreviousTime;
                    CurrentTime = FallbackNode.CurrentTime;
                    return fallbackResult;
                }

                return base.Update(ctx);
            }

            var parent = ctx.Graph;
            PushParameters(parent);

            var eventRangeStart = ctx.SampledEvents.Count;
            var childPose = childGraph.Update(ctx.DeltaTime, updateRange);

            // Surface the child's events so parent conditions can see them
            ctx.SampledEvents.AppendFrom(childGraph.Context.SampledEvents);

            var result = base.Update(ctx);
            var count = Math.Min(childPose.Length, result.Pose.Length);
            childPose.AsSpan(0, count).CopyTo(result.Pose);
            result.SampledEventRange = new(eventRangeStart, ctx.SampledEvents.Count);

            var childRoot = childGraph.Context.RootNode;
            Duration = childRoot.Duration;
            PreviousTime = childRoot.PreviousTime;
            CurrentTime = childRoot.CurrentTime;

            return result;
        }

        private void PushParameters(AnimationGraph parent)
        {
            Debug.Assert(childGraph != null);

            foreach (var name in sharedBoolParameters)
            {
                childGraph!.BoolParameters[name] = parent.BoolParameters[name];
            }

            foreach (var name in sharedFloatParameters)
            {
                childGraph!.FloatParameters[name] = parent.FloatParameters[name];
            }

            foreach (var name in sharedIdParameters)
            {
                childGraph!.IdParameters[name] = parent.IdParameters[name];
            }

            foreach (var name in sharedVectorParameters)
            {
                childGraph!.VectorParameters[name] = parent.VectorParameters[name];
            }

            foreach (var name in sharedTargetParameters)
            {
                childGraph!.TargetParameters[name] = parent.TargetParameters[name];
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
        public virtual bool DisableRootMotionSampling => SelectedOption?.DisableRootMotionSampling ?? false;
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
        {
            var parameterValue = ParameterNode.GetValue(ctx);
            var seed = (int)Math.Floor(Math.Abs(parameterValue));

            // todo: IgnoreInvalidOptions

            var numOptions = OptionNodes.Length;
            if (numOptions == 0)
            {
                return -1;
            }

            if (!HasWeightsSet)
            {
                return seed % numOptions;
            }

            Debug.Assert(OptionWeights.Length == numOptions);

            // Build cumulative bucket boundaries from the byte weights.
            // Zero-weight options exist in shipped data; they are simply never picked.
            Span<int> boundaries = stackalloc int[numOptions];
            var totalWeightedOptions = 0;
            for (var i = 0; i < numOptions; i++)
            {
                totalWeightedOptions += OptionWeights[i];
                boundaries[i] = totalWeightedOptions;
            }

            if (totalWeightedOptions == 0)
            {
                return seed % numOptions;
            }

            var weightedIdx = seed % totalWeightedOptions;

            // Find the bucket that contains the rolled index
            for (var i = 0; i < numOptions; i++)
            {
                if (weightedIdx < boundaries[i])
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
    #endregion
}
