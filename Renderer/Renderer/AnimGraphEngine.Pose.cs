using System.Diagnostics;
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
        // SampledEventRange
    }

    partial class PoseNode
    {
        public int LoopCount;
        public float Duration;   /* Seconds */
        public float CurrentTime; /* Percent */
        public float PreviousTime;  /* Percent */

        public FrameBone[] ModelSpaceTransforms = [];

        public override void Initialize(GraphContext ctx)
        {
            LoopCount = 0;
            Duration = 0f;
            CurrentTime = 1f;
            PreviousTime = 1f;

            ModelSpaceTransforms = new FrameBone[ctx.Controller.BindPose.Length];
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
                result.Pose[i] = FrameBone.Identity;
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

            if (IsLooping)
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
