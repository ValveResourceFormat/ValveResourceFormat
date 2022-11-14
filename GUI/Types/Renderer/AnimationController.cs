using System;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace GUI.Types.Renderer
{
    public class AnimationController
    {
        private Action<Animation, int> updateHandler;
        private Animation activeAnimation;

        public float Time { get; private set; }
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

        public void Update(float timeStep)
        {
            if (!IsPaused)
            {
                Time += timeStep;
                updateHandler(activeAnimation, Frame);
            }
        }

        public void SetAnimation(Animation animation)
        {
            activeAnimation = animation;
            Time = 0f;
            updateHandler(activeAnimation, Frame);
        }

        public void RegisterUpdateHandler(Action<Animation, int> handler)
        {
            updateHandler = handler;
        }
    }
}
