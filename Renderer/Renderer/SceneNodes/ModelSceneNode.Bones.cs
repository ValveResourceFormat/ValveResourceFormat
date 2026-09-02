using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Carries the posed skeleton to the GPU: the bone matrix buffer the vertex shader skins against,
    /// and the bounding box that follows the pose.
    /// </summary>
    public partial class ModelSceneNode
    {
        /// <summary>Whether this model has an active GPU bone matrix buffer, i.e. has animations loaded.</summary>
        private bool IsAnimated => boneMatricesGpu != null;

        private StorageBuffer? boneMatricesGpu;

        private readonly int boneCount;

        private readonly ReadOnlyMemory<int> remappingTable;

        /// <summary>
        /// The mesh-local bone index for the given model-level bone index, within that mesh's slice of
        /// the remapping table.
        /// </summary>
        private int GetMeshBoneIndex(int modelBoneIndex, RenderableMesh mesh)
            => remappingTable.Span.Slice(mesh.MeshBoneOffset, mesh.MeshBoneCount).IndexOf(modelBoneIndex);

        /// <summary>
        /// Writes the current pose into the GPU bone matrix buffer, remapped from model bone order into
        /// the mesh bone order the vertex shader indexes, and refits the bounding box to it.
        /// </summary>
        private void UploadBoneMatrices()
        {
            Debug.Assert(boneMatricesGpu != null, "boneMatricesGpu should not be null when IsAnimated is true");

            var meshBoneCount = remappingTable.Length;

            var floatBufferSizeMeshBones = meshBoneCount * 12;
            var floatBufferSizeModelBones = boneCount * 16;

            using var floatBuffer = new RentedBuffer<float>(floatBufferSizeMeshBones + floatBufferSizeModelBones);

            var meshBones = MemoryMarshal.Cast<float, OpenTK.Mathematics.Matrix3x4>(floatBuffer.Span[..floatBufferSizeMeshBones]);
            var modelBones = MemoryMarshal.Cast<float, Matrix4x4>(floatBuffer.Span[floatBufferSizeMeshBones..]);

            AnimationController.GetSkinningMatrices(modelBones);

            var meshBoneRemap = remappingTable.Span;

            for (var i = 0; i < meshBoneCount; i++)
            {
                var modelBoneIndex = meshBoneRemap[i];
                var modelBoneExists = modelBoneIndex < boneCount && modelBoneIndex != -1;

                if (modelBoneExists)
                {
                    meshBones[i] = modelBones[modelBoneIndex].To3x4();
                }
            }

            boneMatricesGpu.Update(floatBuffer.ByteArray, 0, floatBufferSizeMeshBones * sizeof(float));

            UpdateAnimatedBoundingBox();
        }

        private void SetupBoneMatrixBuffers()
        {
            if (boneCount == 0 || boneMatricesGpu != null)
            {
                return;
            }

            boneMatricesGpu = new StorageBuffer(ReservedBufferSlots.BoneTransforms, nameof(ReservedBufferSlots.BoneTransforms));
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

                // A bone's scale is uniform in practice, so its X axis stands in for all three.
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
