using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace GUI.Types.Renderer
{
    class AnimationController
    {
        private Action<Animation, int> updateHandler = (_, __) => { };

        public float FrametimeMultiplier { get; set; } = 1.0f;
        public float Time { get; private set; }
        private bool shouldUpdate;

        public Animation ActiveAnimation { get; private set; }
        public AnimationFrameCache FrameCache { get; }
        public bool IsPaused { get; set; }
        public int Frame
        {
            get
            {
                if (ActiveAnimation != null && ActiveAnimation.FrameCount != 0)
                {
                    return (int)MathF.Round(Time * ActiveAnimation.Fps) % (ActiveAnimation.FrameCount - 1);
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
                    shouldUpdate = true;
                }
            }
        }

        public AnimationController(Skeleton skeleton, FlexController[] flexControllers)
        {
            FrameCache = new(skeleton, flexControllers);
        }

        public bool Update(float timeStep)
        {
            if (ActiveAnimation == null)
            {
                return false;
            }

            if (IsPaused || ActiveAnimation.FrameCount == 1)
            {
                var res = shouldUpdate;
                shouldUpdate = false;
                return res;
            }

            Time += timeStep * FrametimeMultiplier;
            updateHandler(ActiveAnimation, Frame);
            shouldUpdate = false;
            return true;
        }

        public void SetAnimation(Animation animation)
        {
            FrameCache.Clear();
            ActiveAnimation = animation;
            Time = 0f;
            Frame = 0;
            updateHandler(ActiveAnimation, -1);
        }

        public void PauseLastFrame()
        {
            IsPaused = true;
            Frame = ActiveAnimation == null ? 0 : ActiveAnimation.FrameCount - 1;
        }

        public Frame GetFrame()
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

        public void RegisterUpdateHandler(Action<Animation, int> handler)
        {
            updateHandler = handler;
        }

        public void GetBoneMatrices(Span<Matrix4x4> boneMatrices, bool bindPose = false)
        {
            if (boneMatrices.Length < FrameCache.Skeleton.Bones.Length)
            {
                throw new ArgumentException("Length of array is smaller than the number of bones");
            }

            var frame = bindPose ? null : GetFrame();

            foreach (var root in FrameCache.Skeleton.Roots)
            {
                GetAnimationMatrixRecursive(root, Matrix4x4.Identity, frame, boneMatrices);
            }
        }

        private static void GetAnimationMatrixRecursive(Bone bone, Matrix4x4 bindPose, Frame frame, Span<Matrix4x4> boneMatrices)
        {
            if (frame != null)
            {
                var transform = frame.Bones[bone.Index];
                bindPose = Matrix4x4.CreateScale(transform.Scale)
                    * Matrix4x4.CreateFromQuaternion(transform.Angle)
                    * Matrix4x4.CreateTranslation(transform.Position)
                    * bindPose;
            }
            else
            {
                bindPose = bone.BindPose * bindPose;
            }

            boneMatrices[bone.Index] = bindPose;

            foreach (var child in bone.Children)
            {
                GetAnimationMatrixRecursive(child, bindPose, frame, boneMatrices);
            }
        }
    }
}
