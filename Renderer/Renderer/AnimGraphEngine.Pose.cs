using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.Renderer.AnimLib
{
    class Pose
    {
        public enum PoseState
        {
            Unset,
            ReferencePose,
            ZeroPose,
        }

        public Skeleton Skeleton { get; private set; }
        Transform[] parentSpaceTransforms = [];
        Transform[] modelSpaceTransforms = [];
        PoseState state = PoseState.Unset;

        /// <summary>Creates a pose for <paramref name="skeleton"/> and sets the initial state.</summary>
        public Pose(Skeleton skeleton, PoseState initialState = PoseState.Unset)
        {
            Debug.Assert(skeleton != null);
            Skeleton = skeleton;
            parentSpaceTransforms = new Transform[skeleton.ParentSpaceReferencePose.Length];
            Reset(initialState);
        }

        /// <summary>Copy data from another pose.</summary>
        public void CopyFrom(Pose rhs)
        {
            Skeleton = rhs.Skeleton;
            parentSpaceTransforms = rhs.parentSpaceTransforms.ToArray();
            modelSpaceTransforms = rhs.modelSpaceTransforms.ToArray();
            state = rhs.state;
        }

        /// <summary>Swap contents with <paramref name="rhs"/>.</summary>
        public void SwapWith(Pose rhs)
        {
            var tmpSkeleton = Skeleton;
            Skeleton = rhs.Skeleton;
            rhs.Skeleton = tmpSkeleton;

            var tmpState = state;
            state = rhs.state;
            rhs.state = tmpState;

            (parentSpaceTransforms, rhs.parentSpaceTransforms) = (rhs.parentSpaceTransforms, parentSpaceTransforms);
            (modelSpaceTransforms, rhs.modelSpaceTransforms) = (rhs.modelSpaceTransforms, modelSpaceTransforms);
        }

        public void ChangeSkeleton(Skeleton skeleton)
        {
            Debug.Assert(skeleton != null);

            if (Skeleton == skeleton)
            {
                return;
            }

            Skeleton = skeleton;
            parentSpaceTransforms = new Transform[skeleton.ParentSpaceReferencePose.Length];
            modelSpaceTransforms = [];
            state = PoseState.Unset;
        }

        public void Reset(PoseState initialState, bool calculateModelSpacePose = false)
        {
            switch (initialState)
            {
                case PoseState.ReferencePose:
                    {
                        SetToReferencePose(calculateModelSpacePose);
                    }
                    break;

                case PoseState.ZeroPose:
                    {
                        SetToZeroPose(calculateModelSpacePose);
                    }
                    break;

                default:
                    {
                        // Leave memory intact, just change state
                        state = PoseState.Unset;
                    }
                    break;
            }
        }

        public void SetToReferencePose(bool setGlobalPose)
        {
            Debug.Assert(Skeleton != null);
            parentSpaceTransforms = Skeleton.ParentSpaceReferencePose.ToArray();

            if (setGlobalPose)
            {
                modelSpaceTransforms = Skeleton.ModelSpaceReferencePose.ToArray();
            }
            else
            {
                modelSpaceTransforms = [];
            }

            state = PoseState.ReferencePose;
        }

        public void SetToZeroPose(bool setGlobalPose)
        {
            Debug.Assert(Skeleton != null);
            var numBones = Skeleton.ParentSpaceReferencePose.Length;
            parentSpaceTransforms = Enumerable.Repeat(Transform.Identity, numBones).ToArray();

            if (setGlobalPose)
            {
                modelSpaceTransforms = parentSpaceTransforms.ToArray();
            }
            else
            {
                modelSpaceTransforms = [];
            }

            state = PoseState.ZeroPose;
        }

        /// <summary>Calculate model-space transforms for the requested LOD (number of relevant bones).</summary>
        public void CalculateModelSpaceTransforms(int numRelevantBones)
        {
            Debug.Assert(Skeleton != null);

            var numTotalBones = parentSpaceTransforms.Length;
            modelSpaceTransforms = new Transform[numTotalBones];

            if (numTotalBones == 0)
            {
                return;
            }

            modelSpaceTransforms[0] = parentSpaceTransforms[0];
            for (var boneIdx = 1; boneIdx < numRelevantBones; boneIdx++)
            {
                var parentIdx = Skeleton.ParentIndices[boneIdx];
                Debug.Assert(parentIdx < boneIdx);

                // Compose parent-space transform with parent's model-space transform
                modelSpaceTransforms[boneIdx] = Compose(parentSpaceTransforms[boneIdx], modelSpaceTransforms[parentIdx]);
            }
        }

        public Transform GetModelSpaceTransform(int boneIdx)
        {
            Debug.Assert(Skeleton != null);
            Debug.Assert(boneIdx < Skeleton.ParentSpaceReferencePose.Length);

            if (modelSpaceTransforms.Length > 0)
            {
                return modelSpaceTransforms[boneIdx];
            }

            // Walk parent chain and accumulate
            var parents = new int[Skeleton.ParentSpaceReferencePose.Length];
            var nextEntry = 0;

            var parentIdx = Skeleton.ParentIndices[boneIdx];
            while (parentIdx != -1)
            {
                parents[nextEntry++] = parentIdx;
                parentIdx = Skeleton.ParentIndices[parentIdx];
            }

            var boneModelSpaceTransform = parentSpaceTransforms[boneIdx];
            if (nextEntry > 0)
            {
                // Calculate global transform of parent
                var arrayIdx = nextEntry - 1;
                parentIdx = parents[arrayIdx--];
                var parentModelSpaceTransform = parentSpaceTransforms[parentIdx];
                for (; arrayIdx >= 0; arrayIdx--)
                {
                    var nextIdx = parents[arrayIdx];
                    var nextTransform = parentSpaceTransforms[nextIdx];
                    parentModelSpaceTransform = Compose(nextTransform, parentModelSpaceTransform);
                }

                // Calculate global transform of bone
                boneModelSpaceTransform = Compose(boneModelSpaceTransform, parentModelSpaceTransform);
            }

            return boneModelSpaceTransform;
        }

        public Transform GetTransform(int boneIdx)
        {
            return parentSpaceTransforms[boneIdx];
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
        public Matrix4x4[] Pose;
        public Matrix4x4 RootMotionDelta;
        // SampledEventRange
    }

    partial class PoseNode
    {
        public int LoopCount;
        public float Duration;   /* Seconds */
        public float CurrentTime; /* Percent */
        public float PreviousTime;  /* Percent */

        public Matrix4x4[] ModelSpaceTransforms = [];

        public override void Initialize(GraphContext ctx)
        {
            LoopCount = 0;
            Duration = 0f;
            CurrentTime = 1f;
            PreviousTime = 1f;

            ModelSpaceTransforms = new Matrix4x4[ctx.Controller.BindPose.Length];
        }

        public virtual bool IsValid => true;

        public virtual GraphPoseNodeResult Update(GraphContext ctx)
        {
            return new GraphPoseNodeResult
            {
                Pose = ModelSpaceTransforms,
                RootMotionDelta = Matrix4x4.Identity,
            };
        }
    }

    partial class ReferencePoseNode
    {
        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            var result = base.Update(ctx);
            ctx.Controller.BindPose.CopyTo(result.Pose, 0);
            return result;
        }
    }

    partial class ZeroPoseNode
    {
        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            var result = base.Update(ctx);
            for (var i = 0; i < result.Pose.Length; i++)
            {
                result.Pose[i] = Matrix4x4.Identity;
            }
            return result;
        }
    }

    #region Animation Source Nodes
    partial class ClipNode
    {
        public override AnimationController GetAnimation(GraphContext ctx) => Animation;
        public override bool IsLooping => AllowLooping;
        public override bool DisableRootMotionSampling => !SampleRootMotion;

        public AnimationController Animation;

        public BoolValueNode? ResetTimeValueNode;
        public BoolValueNode? PlayInReverseValueNode;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            Animation = ctx.Controller.Sequences[DataSlotIdx];

            Debug.Assert(Animation.ActiveAnimation != null);
            Duration = Animation.ActiveAnimation.Duration;

            ctx.SetOptionalNodeFromIndex(ResetTimeValueNodeIdx, ref ResetTimeValueNode);
            ctx.SetOptionalNodeFromIndex(PlayInReverseValueNodeIdx, ref PlayInReverseValueNode);
        }

        public override void UpdateSelection(GraphContext ctx)
        {
            //
        }

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            var result = base.Update(ctx);

            Debug.Assert(CurrentTime >= 0f && CurrentTime <= 1f);
            Debug.Assert(Animation.ActiveAnimation != null);

            // Unsynchronized Update

            if (Animation.ActiveAnimation.FrameCount == 1)
            {
                Animation.SamplePoseAtFrame(0, result.Pose);
                return result;
            }

            var resetTime = ResetTimeValueNode?.GetValue(ctx) ?? false;
            if (resetTime)
            {
                CurrentTime = 0f;
                PreviousTime = 0f;
            }

            // todo
            var playInReverse = PlayInReverseValueNode?.GetValue(ctx) ?? false;

            var deltaPercentage = (ctx.DeltaTime * SpeedMultiplier) / Duration;

            PreviousTime = CurrentTime;
            CurrentTime += deltaPercentage;

            if (true /* temp IsLooping*/ )
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
            var frame = Animation.SamplePoseAtPercentage(CurrentTime, result.Pose);


            // root motion
            // frame.Movement.Position;

            return result;
        }
    }

    partial class AnimationPoseNode
    {
        public FloatValueNode? PoseTimeValueNode;
        public AnimationController Animation;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetOptionalNodeFromIndex(PoseTimeValueNodeIdx, ref PoseTimeValueNode);
            Animation = ctx.Controller.Sequences[DataSlotIdx];
            Debug.Assert(Animation.ActiveAnimation != null);
            Duration = Animation.ActiveAnimation.Duration;
            // set to null if skeletons don't match
        }

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            var result = base.Update(ctx);

            Debug.Assert(Animation.ActiveAnimation != null);

            if (Animation.ActiveAnimation.FrameCount == 1)
            {
                Animation.SamplePoseAtFrame(0, result.Pose);
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
                timeValue /= Animation.ActiveAnimation.FrameCount - 1;
            }

            CurrentTime = MathUtils.Saturate(timeValue);
            PreviousTime = CurrentTime;

            Animation.SamplePoseAtPercentage(CurrentTime, result.Pose);
            return result;
        }
    }
    #endregion


    # region Clip Selector Nodes
    // An interface to directly access a selected animation
    // This is needed to ensure certain animation nodes only operate on animations directly
    abstract partial class ClipReferenceNode
    {
        public virtual AnimationController? GetAnimation(GraphContext ctx) => SelectedOption?.GetAnimation(ctx);
        public virtual bool IsLooping => SelectedOption?.IsLooping ?? false;
        public virtual bool DisableRootMotionSampling => SelectedOption?.DisableRootMotionSampling ?? false;
        public ClipReferenceNode? SelectedOption;

        public abstract void UpdateSelection(GraphContext ctx);

        public override GraphPoseNodeResult Update(GraphContext ctx)
        {
            UpdateSelection(ctx);

            if (SelectedOption != null)
            {
                return SelectedOption.Update(ctx);
            }

            return base.Update(ctx);
        }
    }

    partial class ClipSelectorNode
    {
        public ClipReferenceNode[] OptionNodes;
        public BoolValueNode[] ConditionNodes;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodesFromIndexArray(ConditionNodeIndices, ref ConditionNodes);
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

    partial class ParameterizedClipSelectorNode
    {
        public ClipReferenceNode[] OptionNodes;
        public FloatValueNode ParameterNode;

        public override void Initialize(GraphContext ctx)
        {
            base.Initialize(ctx);
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodeFromIndex(ParameterNodeIdx, ref ParameterNode);
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

            // Build cumulative bucket boundaries from the byte weights
            Span<int> boundaries = stackalloc int[numOptions];
            var totalWeightedOptions = 0;
            for (var i = 0; i < numOptions; i++)
            {
                Debug.Assert(OptionWeights[i] > 0);
                totalWeightedOptions += OptionWeights[i];
                boundaries[i] = totalWeightedOptions;
            }

            Debug.Assert(totalWeightedOptions > 0);

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

    // what is this
    partial class TargetSelectorNode
    {
        public override void UpdateSelection(GraphContext ctx)
        {
            // todo
        }
    }
    #endregion
}
