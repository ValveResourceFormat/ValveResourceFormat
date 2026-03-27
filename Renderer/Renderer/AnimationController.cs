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
        public float Time { get; private set { field = value; forceUpdate = true; } }
        private bool forceUpdate;

        /// <summary>Gets the currently active animation, or <see langword="null"/> if none is set.</summary>
        public Animation? ActiveAnimation { get; private set; }

        /// <summary>Gets the frame cache used to retrieve and interpolate animation frames.</summary>
        public AnimationFrameCache FrameCache { get; }

        /// <summary>Gets the decoded animation frame data for the current tick, or <see langword="null"/> when no animation is active.</summary>
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

            if (CurrentSubController is { } subController)
            {
                subController.Handler.IsPaused = IsPaused;
                subController.Handler.Time = Time;

                var updated = subController.Handler.Update(0f);
                if (!updated && !forceUpdate)
                {
                    return false;
                }

                // Pose calculation from AG2 clip
                static void ComputePoseRecursive(Bone bone, Matrix4x4 parentTransform, SubController subController, Span<Matrix4x4> pose)
                {
                    var remapIndex = subController.RemapTable[bone.Index];

                    if (remapIndex != -1)
                    {
                        // Bone is animated in sub-controller, use its pose
                        pose[bone.Index] = subController.Handler.Pose[remapIndex];
                    }
                    else
                    {
                        // Bone is not animated, compute from parent + bind pose
                        pose[bone.Index] = bone.BindPose * parentTransform;
                    }

                    foreach (var child in bone.Children)
                    {
                        ComputePoseRecursive(child, pose[bone.Index], subController, pose);
                    }
                }

                foreach (var root in Skeleton.Roots)
                {
                    if (root.IsProceduralCloth)
                    {
                        continue;
                    }

                    ComputePoseRecursive(root, Matrix4x4.Identity, subController, Pose);
                }


                AnimationFrame = GetFrame();
                updateHandler(ActiveAnimation, Frame);
                forceUpdate = false;
                return true;
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

            if (animation is null && CurrentSubController is { } subController)
            {
                subController.Handler.SetAnimation(null);
                CurrentSubController = null;
                return;
            }

            if (animation is { Clip: { } nmClip })
            {
                var skeletonName = nmClip.SkeletonName;
                if (ExternalSkeletons.TryGetValue(skeletonName, out subController))
                {
                    subController.Handler.SetAnimation(animation);
                    CurrentSubController = subController;
                    return;
                }
            }

            CurrentSubController = null;
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
            if (CurrentSubController is { } subController)
            {
                return subController.Handler.GetFrame();
            }

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

        /// <summary>
        /// The current sub animation controller that is driving animation updates.
        /// </summary>
        public SubController? CurrentSubController { get; private set; }

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

            // Could this be a simpler base type?
            var controller = new AnimationController(skeleton, []);

            ExternalSkeletons[skeletonName] = new(controller, remapTable, debugMap);
        }
    }
}
