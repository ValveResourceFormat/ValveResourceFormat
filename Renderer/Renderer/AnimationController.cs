using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Manages skeletal animation playback and computes animated bone poses.
    /// </summary>
    public partial class AnimationController : BaseAnimationController
    {
        private Action<Animation?, int> updateHandler = (_, __) => { };

        /// <summary>Gets or sets the playback speed multiplier applied to the animation timestep.</summary>
        public float FrametimeMultiplier { get; set; } = 1.0f;

        private bool forceUpdate;

        /// <summary>
        /// The parent animating transform.
        /// </summary>
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

        /// <summary>Gets or sets whether animations should loop when reaching the end.</summary>
        public bool Looping { get; set; } = true;

        /// <summary>
        /// Gets the currently active animation, or <see langword="null"/> if none is set. While a
        /// sub-controller is driving playback (AG2 external skeletons) this reports its animation.
        /// </summary>
        public Animation? ActiveAnimation => driver.ActiveAnimation;

        /// <summary>Gets the frame cache used to retrieve and interpolate animation frames.</summary>
        public AnimationFrameCache FrameCache { get; }

        /// <summary>Gets the decoded animation frame data for the current tick, or <see langword="null"/> when no animation is active.</summary>
        public Frame? AnimationFrame { get; private set; }


        /// <summary>Gets or sets whether animation playback is paused. Changing the value forces a pose update.</summary>
        public bool IsPaused
        {
            get => field;
            set
            {
                forceUpdate = field != value;
                field = value;
            }
        }

        /// <summary>Gets or sets the current frame index of the active animation.</summary>
        public int Frame
        {
            get => driver.Frame;
            set
            {
                driver.Frame = value;
                forceUpdate = true;
            }
        }

        /// <summary>Gets or sets the current playback time in seconds.</summary>
        public float Time
        {
            get => driver.Time;
            set
            {
                driver.Time = value;
                forceUpdate = true;
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
            BlendedFrame = new(skeleton, flexControllers);
            directDriver = new DirectPlaybackDriver(this);
            driver = directDriver;
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

            if (!driver.Update(timeStep, out var animationFrame) && !forceUpdate)
            {
                return false;
            }

            AnimationFrame = animationFrame;
            updateHandler(ActiveAnimation, Frame);
            forceUpdate = false;
            return true;
        }

        /// <summary>
        /// Gets or sets whether the active animation is composed additively over the skeleton bind pose
        /// rather than applied as an absolute pose. <see cref="SetAnimation(Animation?, float)"/> seeds this
        /// from <see cref="Animation.IsAdditive"/>. While a sub-controller is driving playback (AG2 external
        /// skeletons) this delegates to it, so it always reflects what is actually being applied. Changing
        /// the value forces a pose update.
        /// </summary>
        public bool ApplyAdditive
        {
            get => driver.ApplyAdditive;
            set
            {
                // Force an update when the effective value changes, otherwise Update's early-out
                // swallows the toggle and the pose is never recomputed.
                forceUpdate = forceUpdate || driver.ApplyAdditive != value;
                driver.ApplyAdditive = value;
            }
        }

        /// <summary>
        /// Sets the active animation, resets playback to frame zero, and clears the frame cache.
        /// </summary>
        /// <param name="animation">The animation to activate, or <see langword="null"/> to clear.</param>
        public void SetAnimation(Animation? animation)
        {
            SetAnimation(animation, 0f);
        }

        /// <summary>
        /// Sets the active animation with a blend-in time for smooth transitions.
        /// </summary>
        /// <param name="animation">The animation to activate, or <see langword="null"/> to clear.</param>
        /// <param name="blendTime">The time in seconds to blend from previous animations to the new animation.</param>
        public void SetAnimation(Animation? animation, float blendTime)
        {
            var newDriver = animation is { RequiresRetarget: true, TargetSkeletonName: { } targetSkeletonName }
                && retargetDrivers.TryGetValue(targetSkeletonName, out var retargetDriver)
                ? retargetDriver
                : (PlaybackDriver)directDriver;

            if (newDriver != directDriver)
            {
                // The mixer state is no longer what is playing; clear it so a later switch back to a
                // model-skeleton animation cannot blend from a stale clip.
                directDriver.ClearClips();
            }

            driver = newDriver;
            driver.SetAnimation(animation, blendTime);

            forceUpdate = true;
            updateHandler(ActiveAnimation, -1);
        }

        /// <summary>Pauses playback and seeks to the last frame of the active animation.</summary>
        public void PauseLastFrame()
        {
            IsPaused = true;
            Frame = ActiveAnimation == null ? 0 : ActiveAnimation.FrameCount - 1;
        }


        /// <summary>
        /// Registers a callback invoked each time the animation frame changes, receiving the active animation and frame index.
        /// </summary>
        /// <param name="handler">The callback to invoke on each animation update.</param>
        public void RegisterUpdateHandler(Action<Animation?, int> handler)
        {
            updateHandler = handler;
        }

        /// <summary>
        /// The current sub animation controller that is driving animation updates, or
        /// <see langword="null"/> while an animation plays directly on the model skeleton.
        /// </summary>
        public SubController? CurrentSubController => (driver as RetargetPlaybackDriver)?.Sub;

        /// <summary>
        /// Represents a sub-animation controller that drives animation from an external skeleton.
        /// </summary>
        /// <param name="Handler">The animation controller managing the external skeleton.</param>
        /// <param name="RemapTable">Bone index mapping from parent to child skeleton.</param>
        /// <param name="DebugMap">Bone name mapping for debugging purposes.</param>
        public record struct SubController(AnimationController Handler, int[] RemapTable, Dictionary<string, string?> DebugMap)
        {
            /// <summary>The sub controller skeleton.</summary>
            public readonly Skeleton Skeleton => Handler.Skeleton;

            /// <summary>Bone name mapping for debugging.</summary>
            public readonly Dictionary<string, string?> DebugMap { get; } = DebugMap;
        }

        /// <summary>
        /// Gets the collection of external skeletons registered for sub-animation control, indexed by skeleton name.
        /// </summary>
        public Dictionary<string, SubController> ExternalSkeletons { get; } = [];

        /// <summary>
        /// Registers an external skeleton for sub-animation control, creating a bone remapping table.
        /// </summary>
        /// <param name="skeletonName">The name identifying the external skeleton.</param>
        /// <param name="skeleton">The external skeleton to register.</param>
        public void RegisterExternalSkeleton(string skeletonName, Skeleton skeleton)
        {
            var sourceBoneCount = skeleton.Bones.Length;
            var destinationBoneCount = Skeleton.Bones.Length;

            var remapTable = new int[destinationBoneCount];
            var debugMap = new Dictionary<string, string?>(destinationBoneCount);

            var nameToIndex = new Dictionary<uint, int>(sourceBoneCount);

            for (var i = 0; i < sourceBoneCount; i++)
            {
                var name = skeleton.Bones[i].Name;
                nameToIndex[StringToken.Store(name)] = i;
            }

            for (var i = 0; i < destinationBoneCount; i++)
            {
                var name = Skeleton.Bones[i].Name;
                var hash = StringToken.Store(name);

                remapTable[i] = -1;
                debugMap[name] = null;

                if (nameToIndex.TryGetValue(hash, out var idx))
                {
                    remapTable[i] = idx;
                    debugMap[name] = skeleton.Bones[idx].Name;
                }
            }

            var controller = new AnimationController(skeleton, [])
            {
                Looping = Looping,
            };

            var subController = new SubController(controller, remapTable, debugMap);
            ExternalSkeletons[skeletonName] = subController;
            retargetDrivers[skeletonName] = new RetargetPlaybackDriver(this, subController);
        }

    }
}
