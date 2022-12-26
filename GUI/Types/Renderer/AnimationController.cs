using System;
using System.Numerics;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace GUI.Types.Renderer
{
    public class AnimationController
    {
        private readonly AnimationFrameCache animationFrameCache;
        private Action<Animation, int> updateHandler = (_, __) => { };
        private Animation activeAnimation;
        private float Time;

        public bool IsPaused { get; set; }
        public int Frame
        {
            get
            {
                if (activeAnimation != null)
                {
                    return (int)Math.Round(Time * activeAnimation.Fps) % activeAnimation.FrameCount;
                }
                return 0;
            }
            set
            {
                if (activeAnimation != null)
                {
                    Time = value / activeAnimation.Fps;
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

            if (!IsPaused)
            {
                Time += timeStep;
                updateHandler(activeAnimation, Frame);
            }

            return true;
        }

        public void SetAnimation(Animation animation)
        {
            animationFrameCache.Clear();
            activeAnimation = animation;
            Time = 0f;
            updateHandler(activeAnimation, -1);
        }

        public void PauseLastFrame()
        {
            IsPaused = true;
            Frame = activeAnimation == null ? 0 : activeAnimation.FrameCount - 1;
        }

        public Matrix4x4[] GetAnimationMatrices(Skeleton skeleton)
            => activeAnimation.GetAnimationMatrices(animationFrameCache, Time, skeleton);

        public void RegisterUpdateHandler(Action<Animation, int> handler)
        {
            updateHandler = handler;
        }
    }
}
