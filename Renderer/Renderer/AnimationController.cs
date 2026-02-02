using System.Diagnostics;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Controls the playback of a single sequence on a skeleton.
    /// </summary>
    public class AnimationController : BaseAnimationController
    {
        private Action<Animation?, int> updateHandler = (_, __) => { };

        public float FrametimeMultiplier { get; set; } = 1.0f;
        public float Time { get; private set; }
        private bool forceUpdate;

        public Animation? ActiveAnimation { get; private set; }
        public AnimationFrameCache FrameCache { get; }

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
            : base(skeleton)
        {
            FrameCache = new(skeleton, flexControllers);
        }

        public override bool Update(float timeStep)
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

            foreach (var root in Skeleton.Roots)
            {
                if (root.IsProceduralCloth)
                {
                    continue;
                }

                GetBoneMatricesRecursive(root, Matrix4x4.Identity, AnimationFrame, Pose);
            }

            return true;
        }

        public virtual void SetAnimation(Animation? animation)
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

        internal void SamplePoseAtFrame(int i, Matrix4x4[] pose)
        {
            Debug.Assert(ActiveAnimation != null);
            var frame = FrameCache.GetFrame(ActiveAnimation, i);

            foreach (var root in Skeleton.Roots)
            {
                GetBoneMatricesRecursive(root, Matrix4x4.Identity, frame, pose);
            }
        }

        internal Frame SamplePoseAtPercentage(float cycle, Matrix4x4[] pose)
        {
            Debug.Assert(ActiveAnimation != null);
            Debug.Assert(cycle >= 0f && cycle <= 1f);

            var time = cycle * ActiveAnimation.Duration;
            var frame = FrameCache.GetInterpolatedFrame(ActiveAnimation, time);

            foreach (var root in Skeleton.Roots)
            {
                GetBoneMatricesRecursive(root, Matrix4x4.Identity, frame, pose);
            }

            return frame;
        }
    }
}
