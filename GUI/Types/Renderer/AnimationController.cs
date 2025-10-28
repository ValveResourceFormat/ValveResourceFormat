using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace GUI.Types.Renderer
{
    class AnimationController
    {
        private Action<Animation?, int> updateHandler = (_, __) => { };

        public float FrametimeMultiplier { get; set; } = 1.0f;
        public float Time { get; private set; }
        private bool forceUpdate;

        public Animation? ActiveAnimation { get; private set; }
        public AnimationFrameCache FrameCache { get; }

        /// <summary>
        /// The skeleton skinning bind pose.
        /// </summary>
        public Matrix4x4[] BindPose { get; }

        /// <summary>
        /// The skeleton inverse bind pose.
        /// </summary>
        private Matrix4x4[] InverseBindPose { get; }

        /// <summary>
        /// The flattened worldspace transform of each bone, according to the current animation frame.
        /// </summary>
        public Matrix4x4[] Pose { get; }

        public Frame? AnimationFrame { get; private set; }

        private bool isPaused;
        public bool IsPaused
        {
            get => isPaused;
            set
            {
                isPaused = value;
                forceUpdate = !value;
            }
        }

        public int Frame
        {
            get
            {
                if (ActiveAnimation != null && ActiveAnimation.FrameCount > 1)
                {
                    return (int)MathF.Round(Time * ActiveAnimation.Fps) % ActiveAnimation.FrameCount;
                }
                return 0;
            }
            set
            {
                if (ActiveAnimation != null)
                {
                    Time = ActiveAnimation.Fps != 0
                        ? value / ActiveAnimation.Fps
                        : 0f;
                    forceUpdate = true;
                }
            }
        }

        public AnimationController(Skeleton skeleton, FlexController[] flexControllers)
        {
            FrameCache = new(skeleton, flexControllers);

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

        public bool Update(float timeStep)
        {
            if ((ActiveAnimation == null || IsPaused || ActiveAnimation.FrameCount == 1) && !forceUpdate)
            {
                return false;
            }

            if (!IsPaused)
            {
                Time += timeStep * FrametimeMultiplier;
            }

            AnimationFrame = GetFrame();
            updateHandler(ActiveAnimation, Frame);
            forceUpdate = false;

            if (AnimationFrame == null)
            {
                BindPose.AsSpan().CopyTo(Pose);
                return true;
            }

            foreach (var root in FrameCache.Skeleton.Roots)
            {
                if (root.IsProceduralCloth)
                {
                    continue;
                }

                GetBoneMatricesRecursive(root, Matrix4x4.Identity, AnimationFrame, Pose);
            }

            return true;
        }

        public void SetAnimation(Animation? animation)
        {
            FrameCache.Clear();
            ActiveAnimation = animation;
            forceUpdate = true;
            Time = 0f;
            Frame = 0;
            updateHandler(ActiveAnimation, -1);
        }

        public void PauseLastFrame()
        {
            IsPaused = true;
            Frame = ActiveAnimation == null ? 0 : ActiveAnimation.FrameCount - 1;
        }

        public Frame? GetFrame()
        {
            if (ActiveAnimation == null)
            {
                return null;
            }
            else if (IsPaused)
            {
                return FrameCache.GetFrame(ActiveAnimation, Frame);
            }
            else
            {
                return FrameCache.GetInterpolatedFrame(ActiveAnimation, Time);
            }
        }

        public void RegisterUpdateHandler(Action<Animation?, int> handler)
        {
            updateHandler = handler;
        }

        private static void GetBoneMatricesRecursive(Bone bone, Matrix4x4 parent, Frame? frame, Span<Matrix4x4> boneMatrices)
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

        private static void GetInverseBindPoseRecursive(Bone bone, Matrix4x4 parent, Span<Matrix4x4> boneMatrices)
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
            var skeleton = FrameCache.Skeleton;

            for (var i = 0; i < Pose.Length; i++)
            {
                modelBones[i] = InverseBindPose[i] * Pose[i];
            }

            // Copy procedural cloth node transforms from a animated root bone
            if (skeleton.ClothSimulationRoot is not null)
            {
                foreach (var clothNode in skeleton.Roots)
                {
                    if (clothNode.IsProceduralCloth)
                    {
                        modelBones[clothNode.Index] = modelBones[skeleton.ClothSimulationRoot.Index];
                    }
                }
            }
        }
    }
}
