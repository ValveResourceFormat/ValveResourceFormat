namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Base class for computing animated bone poses
    /// </summary>
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

        /// <summary>
        /// Initializes a new <see cref="BaseAnimationController"/> for the given skeleton,
        /// computing the bind pose and inverse bind pose matrices.
        /// </summary>
        /// <param name="skeleton">The skeleton whose bones define the rig.</param>
        public BaseAnimationController(Skeleton skeleton)
        {
            Skeleton = skeleton;
            BindPose = new Matrix4x4[skeleton.Bones.Length];
            InverseBindPose = new Matrix4x4[skeleton.Bones.Length];
            Pose = new Matrix4x4[skeleton.Bones.Length];

            foreach (var root in skeleton.Roots)
            {
                FramePose.ComputeWorldSubtree(root, Matrix4x4.Identity, null, BindPose);
                GetInverseBindPoseRecursive(root, Matrix4x4.Identity, InverseBindPose);
            }

            BindPose.CopyTo(Pose, 0);
        }

        /// <summary>
        /// Updates the animation controller, advancing the animation by the given time step.
        /// </summary>
        /// <param name="timeStep">Elapsed time in seconds since the last update.</param>
        /// <returns><see langword="true"/> if the pose was updated; <see langword="false"/> otherwise.</returns>
        public virtual bool Update(float timeStep) => false;

        /// <summary>
        /// Recursively computes the inverse bind pose matrix for each bone in the hierarchy.
        /// </summary>
        /// <param name="bone">The current bone to process.</param>
        /// <param name="parent">The accumulated inverse bind pose from the parent.</param>
        /// <param name="boneMatrices">The output array to store computed inverse bind pose matrices.</param>
        protected static void GetInverseBindPoseRecursive(Bone bone, Matrix4x4 parent, Span<Matrix4x4> boneMatrices)
        {
            boneMatrices[bone.Index] = parent * bone.InverseBindPose;

            foreach (var child in bone.Children)
            {
                GetInverseBindPoseRecursive(child, boneMatrices[bone.Index], boneMatrices);
            }
        }

        /// <summary>
        /// Procedural cloth root bones are excluded from pose evaluation because there is no cloth
        /// simulation. Instead of leaving them frozen at bind pose, rigidly attach them to the cloth
        /// simulation root so they follow the body. This mirrors how <see cref="GetSkinningMatrices"/>
        /// skins the cloth mesh, and keeps the skeleton overlay consistent with the rendered mesh.
        /// </summary>
        protected void ApplyClothRootPose()
        {
            var clothSimRoot = Skeleton.ClothSimulationRoot;
            if (clothSimRoot is null)
            {
                return;
            }

            var delta = InverseBindPose[clothSimRoot.Index] * Pose[clothSimRoot.Index];

            foreach (var root in Skeleton.Roots)
            {
                if (root.IsProceduralCloth)
                {
                    Pose[root.Index] = root.BindPose * delta;
                }
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

            // Copy procedural cloth node transforms from an animated root bone
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
