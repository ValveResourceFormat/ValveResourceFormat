using System.Diagnostics;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

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
}
