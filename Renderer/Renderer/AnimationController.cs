using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Manages skeletal animation playback and computes animated bone poses.
    /// </summary>
    public class AnimationController : BaseAnimationController
    {
        private Action<Animation?, int> updateHandler = (_, __) => { };

        /// <summary>Gets or sets the playback speed multiplier applied to the animation timestep.</summary>
        public float FrametimeMultiplier { get; set; } = 1.0f;

        /// <summary>Gets the current playback time in seconds.</summary>
        public float Time { get; private set; }
        private bool forceUpdate;

        /// <summary>Gets the currently active animation, or <see langword="null"/> if none is set.</summary>
        public Animation? ActiveAnimation { get; private set; }

        /// <summary>Gets the frame cache used to retrieve and interpolate animation frames.</summary>
        public AnimationFrameCache FrameCache { get; }

        public Frame? AnimationFrame { get; private set; }

        private bool isPaused;

        /// <summary>Gets or sets whether animation playback is paused. Setting to <see langword="false"/> forces an immediate pose update.</summary>
        public bool IsPaused
        {
            get => isPaused;
            set
            {
                isPaused = value;
                forceUpdate = !value;
            }
        }

        /// <summary>Gets or sets the current frame index of the active animation.</summary>
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

        /// <summary>
        /// Initializes a new <see cref="AnimationController"/> for the given skeleton and flex controllers,
        /// computing the bind pose and inverse bind pose matrices.
        /// </summary>
        /// <param name="skeleton">The skeleton whose bones define the rig.</param>
        /// <param name="flexControllers">The flex controllers used for facial/morph animation.</param>
        public AnimationController(Skeleton skeleton, FlexController[] flexControllers)
            : base(skeleton)
        {
            FrameCache = new(skeleton, flexControllers);
        }

        /// <summary>
        /// Advances the animation by <paramref name="timeStep"/> seconds and recomputes bone poses.
        /// </summary>
        /// <param name="timeStep">Elapsed time in seconds since the last update.</param>
        /// <returns><see langword="true"/> if the pose was updated; <see langword="false"/> if nothing changed.</returns>
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

        /// <summary>
        /// Sets the active animation, resets playback to frame zero, and clears the frame cache.
        /// </summary>
        /// <param name="animation">The animation to activate, or <see langword="null"/> to clear.</param>
        public void SetAnimation(Animation? animation)
        {
            FrameCache.Clear();
            ActiveAnimation = animation;
            forceUpdate = true;
            Time = 0f;
            Frame = 0;
            updateHandler(ActiveAnimation, -1);
        }

        /// <summary>Pauses playback and seeks to the last frame of the active animation.</summary>
        public void PauseLastFrame()
        {
            IsPaused = true;
            Frame = ActiveAnimation == null ? 0 : ActiveAnimation.FrameCount - 1;
        }

        /// <summary>
        /// Returns the animation frame for the current time, using exact frame lookup when paused or interpolation during playback.
        /// </summary>
        /// <returns>The current animation frame, or <see langword="null"/> if no animation is active.</returns>
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

        /// <summary>
        /// Registers a callback invoked each time the animation frame changes, receiving the active animation and frame index.
        /// </summary>
        /// <param name="handler">The callback to invoke on each animation update.</param>
        public void RegisterUpdateHandler(Action<Animation?, int> handler)
        {
            updateHandler = handler;
        }
    }
}
