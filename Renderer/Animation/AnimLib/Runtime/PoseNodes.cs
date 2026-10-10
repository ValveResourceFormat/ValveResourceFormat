using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer.AnimLib
{
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
}
