using System;
using System.Numerics;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace GUI.Types.Renderer
{
    class AnimationController
    {
        private readonly AnimationFrameCache animationFrameCache;
        private Action<Animation, int> updateHandler = (_, __) => { };
        private Animation activeAnimation;
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
                    return (int)Math.Round(Time * activeAnimation.Fps) % activeAnimation.FrameCount;
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

        public AnimationController(Skeleton skeleton)
        {
            animationFrameCache = new(skeleton);
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

            Time += timeStep;
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

        public Matrix4x4[] GetAnimationMatrices(Skeleton skeleton)
        {
            if (IsPaused)
            {
                return activeAnimation.GetAnimationMatrices(animationFrameCache, Frame, skeleton);
            }

            return activeAnimation.GetAnimationMatrices(animationFrameCache, Time, skeleton);
        }

        public void RegisterUpdateHandler(Action<Animation, int> handler)
        {
            updateHandler = handler;
        }
    }
}
