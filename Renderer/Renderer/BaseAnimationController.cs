using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    public class BaseAnimationController
    {
        /// <summary>
        /// The skeleton being animated.
        /// </summary>
        public Skeleton Skeleton { get; }

        /// <summary>
        /// The skeleton skinning bind pose.
        /// </summary>
        public Matrix4x4[] BindPose { get; }

        /// <summary>
        /// The skeleton inverse bind pose.
        /// </summary>
        public Matrix4x4[] InverseBindPose { get; }

        /// <summary>
        /// The flattened worldspace transform of each bone, according to the current animation frame.
        /// </summary>
        public Matrix4x4[] Pose { get; }

        public BaseAnimationController(Skeleton skeleton)
        {
            Skeleton = skeleton;
            BindPose = new Matrix4x4[skeleton.Bones.Length];
            InverseBindPose = new Matrix4x4[skeleton.Bones.Length];
            Pose = new Matrix4x4[skeleton.Bones.Length];

            foreach (var root in skeleton.Roots)
            {
                GetBoneMatricesRecursive(root, Matrix4x4.Identity, null, BindPose);
                GetInverseBindPoseRecursive(root, Matrix4x4.Identity, InverseBindPose);
            }

            BindPose.CopyTo(Pose, 0);
        }

        public virtual bool Update(float timeStep) => false;

        protected static void GetBoneMatricesRecursive(Bone bone, Matrix4x4 parent, Frame? frame, Span<Matrix4x4> boneMatrices)
        {
            var boneTransform = bone.BindPose;

            if (frame != null)
            {
                var frameBone = frame.Bones[bone.Index];
                boneTransform = Matrix4x4.CreateScale(frameBone.Scale)
                    * Matrix4x4.CreateFromQuaternion(frameBone.Angle)
                    * Matrix4x4.CreateTranslation(frameBone.Position);
            }

            boneTransform *= parent;
            boneMatrices[bone.Index] = boneTransform;

            foreach (var child in bone.Children)
            {
                GetBoneMatricesRecursive(child, boneTransform, frame, boneMatrices);
            }
        }

        protected static void GetInverseBindPoseRecursive(Bone bone, Matrix4x4 parent, Span<Matrix4x4> boneMatrices)
        {
            boneMatrices[bone.Index] = parent * bone.InverseBindPose;

            foreach (var child in bone.Children)
            {
                GetInverseBindPoseRecursive(child, boneMatrices[bone.Index], boneMatrices);
            }
        }

        /// <summary>
        /// Get bone matrices in bindpose space.
        /// Bones that do not move from the original location will have an identity matrix.
        /// Thus there will be no transformation in the vertex shader.
        /// </summary>
        public void GetSkinningMatrices(Span<Matrix4x4> modelBones)
        {
            for (var i = 0; i < Pose.Length; i++)
            {
                modelBones[i] = InverseBindPose[i] * Pose[i];
            }

            // Copy procedural cloth node transforms from a animated root bone
            var clothSimRoot = Skeleton.ClothSimulationRoot;
            if (clothSimRoot is not null)
            {
                foreach (var clothNode in Skeleton.Roots)
                {
                    if (clothNode.IsProceduralCloth)
                    {
                        modelBones[clothNode.Index] = modelBones[clothSimRoot.Index];
                    }
                }
            }
        }
    }
}
