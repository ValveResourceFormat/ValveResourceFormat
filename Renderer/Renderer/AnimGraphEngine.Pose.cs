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

    partial class PoseNode
    {
        public int LoopCount;
        public TimeSpan Duration;
        public float CurrentTime; /* Percent */
        public float PreviousTime;  /* Percent */

        public override void Initialize(GraphContext ctx)
        {
            //
        }

        public virtual void Update(GraphContext ctx)
        {
            //
        }
    }


    partial class ParameterizedClipSelectorNode
    {
        public ClipReferenceNode[] OptionNodes;
        public FloatValueNode ParameterNode;

        public override void Initialize(GraphContext ctx)
        {
            ctx.SetNodesFromIndexArray(OptionNodeIndices, ref OptionNodes);
            ctx.SetNodeFromIndex(ParameterNodeIdx, ref ParameterNode);
        }

        public ClipReferenceNode SelectOption(GraphContext ctx)
        {
            var selectedIndex = (int)ParameterNode.GetValue(ctx);

            if (HasWeightsSet)
            {
                // ?
            }

            return OptionNodes[selectedIndex];
        }
    }
}
