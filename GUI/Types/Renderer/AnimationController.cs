using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace GUI.Types.Renderer
{
    class AnimationController
    {
        private readonly AnimationFrameCache animationFrameCache;
        private Action<Animation, int> updateHandler = (_, __) => { };
        private Animation activeAnimation;
        public float FrametimeMultiplier { get; set; } = 1.0f;
        public float Time { get; private set; }
        private bool shouldUpdate;

        public Animation ActiveAnimation => activeAnimation;
        public AnimationFrameCache FrameCache => animationFrameCache;
        public bool IsPaused { get; set; }
        public int Frame
        {
            get
            {
                if (activeAnimation != null && activeAnimation.FrameCount != 0)
                {
                    return (int)MathF.Round(Time * activeAnimation.Fps) % activeAnimation.FrameCount;
                }
                return 0;
            }
            set
            {
                if (activeAnimation != null)
                {
                    Time = activeAnimation.Fps != 0
                        ? value / activeAnimation.Fps
                        : 0f;
                    shouldUpdate = true;
                }
            }
        }

        public AnimationController(Skeleton skeleton, FlexController[] flexControllers)
        {
            animationFrameCache = new(skeleton, flexControllers);
        }

        public bool Update(float timeStep)
        {
            if (activeAnimation == null)
            {
                return false;
            }

            if (IsPaused || activeAnimation.FrameCount == 1)
            {
                var res = shouldUpdate;
                shouldUpdate = false;
                return res;
            }

            Time += timeStep * FrametimeMultiplier;
            updateHandler(activeAnimation, Frame);
            shouldUpdate = false;
            return true;
        }

        public void SetAnimation(Animation animation)
        {
            animationFrameCache.Clear();
            activeAnimation = animation;
            Time = 0f;
            Frame = 0;
            updateHandler(activeAnimation, -1);
        }

        public void PauseLastFrame()
        {
            IsPaused = true;
            Frame = activeAnimation == null ? 0 : activeAnimation.FrameCount - 1;
        }

        public Frame GetFrame()
        {
            if (activeAnimation == null)
            {
                return null;
            }
            else if (IsPaused)
            {
                return animationFrameCache.GetFrame(activeAnimation, Frame);
            }
            else
            {
                return animationFrameCache.GetInterpolatedFrame(activeAnimation, Time);
            }
        }

        public void RegisterUpdateHandler(Action<Animation, int> handler)
        {
            updateHandler = handler;
        }

        public void GetBoneMatrices(Span<Matrix4x4> boneMatrices, bool bindPose = false)
        {
            if (boneMatrices.Length < animationFrameCache.Skeleton.Bones.Length)
            {
                throw new ArgumentException("Length of array is smaller than the number of bones");
            }

            var frame = bindPose ? null : GetFrame();

            foreach (var root in animationFrameCache.Skeleton.Roots)
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
