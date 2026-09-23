using System.Diagnostics;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Carries the posed skeleton to the GPU, and the bounding box that follows the pose.
    /// </summary>
    public partial class ModelSceneNode
    {
        private bool IsAnimated { get; set; }

        private readonly int boneCount;

        private readonly ReadOnlyMemory<int> remappingTable;

        /// <summary>
        /// The mesh-local bone index for the given model-level bone index, within that mesh's slice of
        /// the remapping table.
        /// </summary>
        private int GetMeshBoneIndex(int modelBoneIndex, RenderableMesh mesh)
            => remappingTable.Span.Slice(mesh.MeshBoneOffset, mesh.MeshBoneCount).IndexOf(modelBoneIndex);

        // Slots from a model's transform to its first bone: Source 2 keeps a CTransform in between
        internal const int BoneTransformStart = 2;

        internal int SkinningTransformCount
            => remappingTable.Length == 0 ? 0 : BoneTransformStart - 1 + remappingTable.Length;

        internal uint TransformSlot { get; set; }

        internal void WriteSkinningTransforms(Span<OpenTK.Mathematics.Matrix3x4> destination)
        {
            if (!IsAnimated)
            {
                return;
            }

            var meshBoneCount = remappingTable.Length;

            Debug.Assert(destination.Length == BoneTransformStart - 1 + meshBoneCount);

            var boneTransforms = destination[(BoneTransformStart - 1)..];

            using var floatBuffer = new RentedBuffer<float>(boneCount * 16);
            var modelBones = MemoryMarshal.Cast<float, Matrix4x4>(floatBuffer.Span);

            AnimationController.GetSkinningMatrices(modelBones);

            var meshBoneRemap = remappingTable.Span;

            var identity = Matrix4x4.Identity.To3x4();

            for (var i = 0; i < meshBoneCount; i++)
            {
                var modelBoneIndex = meshBoneRemap[i];
                var modelBoneExists = modelBoneIndex < boneCount && modelBoneIndex != -1;

                boneTransforms[i] = modelBoneExists
                    ? modelBones[modelBoneIndex].To3x4()
                    : identity;
            }
        }

        private void SetupSkinning()
        {
            IsAnimated = boneCount > 0;
        }

        private ModelSceneNode? boneMergeTarget;

        // For each bone of this model, the bone of the same name on the bone merge target, or -1
        private int[] boneMergeTargetBones = [];

        /// <summary>
        /// Poses this model's bones where the bones of the same name on <paramref name="target"/> are, on every update
        /// instead of playing its own animations. This is how wearables follow the character wearing them. Bones the
        /// target does not have keep their bind pose offset from their parent bone.
        /// </summary>
        /// <param name="target">The model to follow, or <see langword="null"/> to go back to this model's own animations.
        /// Both models should share a transform, and the target has to be updated first, which it is when it was added
        /// to the scene before this model.</param>
        public void SetBoneMergeTarget(ModelSceneNode? target)
        {
            boneMergeTarget = target;

            if (target == null)
            {
                boneMergeTargetBones = [];
                AnimationController.SetAnimation(null);

                foreach (var renderer in meshRenderers)
                {
                    renderer.SetSkinningActive(false);
                }

                return;
            }

            var targetBones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var bone in target.AnimationController.Skeleton.Bones)
            {
                targetBones.TryAdd(bone.Name, bone.Index);
            }

            var skeleton = AnimationController.Skeleton;
            boneMergeTargetBones = new int[skeleton.Bones.Length];

            foreach (var bone in skeleton.Bones)
            {
                boneMergeTargetBones[bone.Index] = targetBones.TryGetValue(bone.Name, out var targetIndex) ? targetIndex : -1;
            }

            // Models made to be worn usually have no animations, which is what enables skinning otherwise
            SetupSkinning();

            foreach (var renderer in meshRenderers)
            {
                renderer.SetSkinningActive(IsAnimated);
            }

            UpdateBoneMergePose();
        }

        private void UpdateBoneMergePose()
        {
            Debug.Assert(boneMergeTarget != null);

            var targetPose = boneMergeTarget.AnimationController.Pose;

            foreach (var root in AnimationController.Skeleton.Roots)
            {
                UpdateBoneMergePose(root, Matrix4x4.Identity, targetPose);
            }
        }

        private void UpdateBoneMergePose(Bone bone, Matrix4x4 parentWorld, Matrix4x4[] targetPose)
        {
            var pose = AnimationController.Pose;
            var targetIndex = boneMergeTargetBones[bone.Index];

            pose[bone.Index] = targetIndex >= 0 ? targetPose[targetIndex] : bone.BindPose * parentWorld;

            foreach (var child in bone.Children)
            {
                UpdateBoneMergePose(child, pose[bone.Index], targetPose);
            }
        }

        private void UpdateBoundingBox()
        {
            var first = true;
            foreach (var mesh in meshRenderers)
            {
                LocalBoundingBox = first ? mesh.BoundingBox : LocalBoundingBox.Union(mesh.BoundingBox);
                first = false;
            }
        }

        /// <summary>
        /// Fits the local bounding box to the current pose by placing each bone's authored sphere at that
        /// bone's posed origin.
        /// </summary>
        private void UpdateAnimatedBoundingBox()
        {
            const bool SkipNonSkinningBones = true;

            var spheres = AnimationController.Skeleton.BoneSpheres;

            if (spheres.Length == 0)
            {
                return;
            }

            var pose = AnimationController.Pose;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var anyBoneContributed = false;

            for (var boneIndex = 0; boneIndex < spheres.Length; boneIndex++)
            {
                if (SkipNonSkinningBones && spheres[boneIndex] <= 0f)
                {
                    continue;
                }

                var bone = pose[boneIndex];

                // A bone's scale is uniform in practice; its X axis stands in for all three.
                var scale = bone.AxisScale(0);

                var radius = new Vector3(spheres[boneIndex] * scale);
                var origin = bone.Translation;

                min = Vector3.Min(min, origin - radius);
                max = Vector3.Max(max, origin + radius);
                anyBoneContributed = true;
            }

            if (!anyBoneContributed)
            {
                UpdateBoundingBox();
                return;
            }

            LocalBoundingBox = new AABB(min, max);
        }
    }
}
