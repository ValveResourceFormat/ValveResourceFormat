using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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

        /// <summary>Gets the current playback time in seconds.</summary>
        public float Time { get; private set { field = value; forceUpdate = true; } }
        private bool forceUpdate;

        /// <summary>
        /// The parent animating transform.
        /// </summary>
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

        /// <summary>Gets or sets whether animations should loop when reaching the end.</summary>
        public bool Looping { get; set; }

        /// <summary>Gets the currently active animation, or <see langword="null"/> if none is set.</summary>
        public Animation? ActiveAnimation { get; private set; }

        /// <summary>Represents an animation clip with its playback state.</summary>
        private record class Clip(Animation Animation)
        {
            public float Time { get; set; }
            public bool IsPaused { get; set; }
            public bool Looping { get; set; }
            public float Weight { get; set; } = 1f;
            public float BlendTime { get; set; }

            public int Frame
            {
                get
                {
                    if (Animation.FrameCount > 1)
                    {
                        return (int)MathF.Round(Time * Animation.Fps) % Animation.FrameCount;
                    }
                    return 0;
                }
                set
                {
                    Time = Animation.Fps != 0 ? value / Animation.Fps : 0f;
                }
            }
        }

        private Clip? activeClip;
        private readonly List<Clip> previousClips = [];
        private readonly Frame BlendedFrame;
        private float currentBlendTime;

        /// <summary>
        /// Gets or sets the tilt-twist constraints that are applied when animations update.
        /// </summary>
        public TiltTwistConstraint[] TwistConstraints { get; set; } = [];

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
            BlendedFrame = new Frame(skeleton, flexControllers);
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

            timeStep *= FrametimeMultiplier;

            if (!IsPaused && activeClip != null)
            {
                activeClip.Time += timeStep;

                if (!activeClip.Looping)
                {
                    var lastFrame = ActiveAnimation!.FrameCount - 1;
                    var maxTime = lastFrame / ActiveAnimation.Fps;

                    if (activeClip.Time > maxTime)
                    {
                        activeClip.IsPaused = true;
                        activeClip.Frame = lastFrame;
                    }
                }

                IsPaused = activeClip.IsPaused;
                Frame = activeClip.Frame;

                // Update time for previous clips so they continue playing during blend
                foreach (var prevClip in previousClips)
                {
                    if (!prevClip.IsPaused)
                    {
                        prevClip.Time += timeStep;
                    }
                }

                // Distribute blend weights over time
                if (previousClips.Count > 0)
                {
                    currentBlendTime -= timeStep;

                    if (currentBlendTime <= 0f)
                    {
                        previousClips.Clear();
                        activeClip.Weight = 1f;
                    }
                    else
                    {
                        // Calculate blend progress (0 = start of blend, 1 = end of blend)
                        var t = previousClips[0].BlendTime > 0f
                            ? 1f - Math.Clamp(currentBlendTime / previousClips[0].BlendTime, 0f, 1f)
                            : 1f;

                        // Apply smoothstep for smoother weight transition
                        var blendProgress = t * t * (3f - 2f * t);

                        // Active animation weight increases from 0 to 1
                        activeClip.Weight = blendProgress;

                        // Previous animations weight decreases from 1 to 0
                        var remainingWeight = 1f - blendProgress;
                        var weightPerPrevious = remainingWeight / previousClips.Count;

                        foreach (var prevClip in previousClips)
                        {
                            prevClip.Weight = weightPerPrevious;
                        }
                    }
                }
                else
                {
                    activeClip.Weight = 1f;
                }

                var sum = activeClip.Weight + previousClips.Sum(c => c.Weight);
                Debug.Assert(sum > 0f, "Total blend weight should be greater than zero.");
                Debug.Assert(Math.Abs(sum - 1f) < 0.01f, $"Total blend weight should be approximately 1. Found: {sum}");
            }

            if (CurrentSubController is { } subController)
            {
                subController.Handler.IsPaused = IsPaused;
                // subController.Handler.Time = Time;
                subController.Handler.Looping = Looping;

                var updated = subController.Handler.Update(timeStep);
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

                    ComputePoseRecursive(root, Transform, subController, Pose);
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

                GetBoneMatricesRecursive(root, Transform, AnimationFrame, Pose);
            }

            return true;
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
            if (animation is null && CurrentSubController is { } subController)
            {
                subController.Handler.SetAnimation(null);
                CurrentSubController = null;
                ActiveAnimation = null;
                activeClip = null;
                previousClips.Clear();
                FrameCache.Clear();
                forceUpdate = true;
                updateHandler(ActiveAnimation, -1);
                return;
            }

            if (animation is { Clip: { } nmClip })
            {
                var skeletonName = nmClip.SkeletonName;
                if (ExternalSkeletons.TryGetValue(skeletonName, out subController))
                {
                    subController.Handler.SetAnimation(animation, blendTime);
                    CurrentSubController = subController;
                    ActiveAnimation = animation;
                    forceUpdate = true;
                    Time = 0f;
                    Frame = 0;
                    updateHandler(ActiveAnimation, -1);
                    return;
                }
            }

            CurrentSubController = null;
            FrameCache.PurgeCache();

            // Handle blending
            if (blendTime > 0f && activeClip != null)
            {
                // Move current clip to previous clips for blending
                previousClips.Clear();
                activeClip.BlendTime = blendTime;
                previousClips.Add(activeClip);
                currentBlendTime = blendTime;
            }
            else
            {
                // No blending - clear previous clips
                previousClips.Clear();
                FrameCache.Clear();
            }

            ActiveAnimation = animation;
            activeClip = animation != null ? new Clip(animation) { Looping = Looping } : null;
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
        /// Returns the animation frame for the current time, using exact frame lookup when paused or interpolation during playback.
        /// </summary>
        /// <returns>The current animation frame, or <see langword="null"/> if no animation is active.</returns>
        public Frame? GetFrame()
        {
            if (CurrentSubController is { } subController)
            {
                return subController.Handler.GetFrame();
            }

            if (activeClip == null)
            {
                return null;
            }

            // Get active animation frame
            var activeFrame = SampleFrame(activeClip);

            // No blending needed if no previous clips
            if (previousClips.Count == 0)
            {
                return activeFrame;
            }

            // Use pre-calculated weights from Update
            BlendedFrame.FrameIndex = activeFrame.FrameIndex;

            // Start with active animation
            for (var i = 0; i < activeFrame.Bones.Length; i++)
            {
                BlendedFrame.Bones[i] = activeFrame.Bones[i];
            }

            for (var i = 0; i < activeFrame.Datas.Length; i++)
            {
                BlendedFrame.Datas[i] = activeFrame.Datas[i];
            }

            // Blend in previous clips using their pre-calculated weights
            var totalWeight = activeClip.Weight;
            foreach (var prevClip in previousClips)
            {
                var prevFrame = SampleFrame(prevClip);
                var blendFactor = prevClip.Weight / (totalWeight + prevClip.Weight);

                for (var i = 0; i < prevFrame.Bones.Length; i++)
                {
                    BlendedFrame.Bones[i] = BlendedFrame.Bones[i].Blend(prevFrame.Bones[i], blendFactor);
                }

                for (var i = 0; i < prevFrame.Datas.Length; i++)
                {
                    BlendedFrame.Datas[i] = float.Lerp(BlendedFrame.Datas[i], prevFrame.Datas[i], blendFactor);
                }

                totalWeight += prevClip.Weight;
            }

            return BlendedFrame;
        }

        private Frame SampleFrame(Clip clip)
        {
            var ignoreCache = clip.Animation != ActiveAnimation;

            try
            {
                if (ignoreCache)
                {
                    FrameCache.PurgeCache();
                }

                return clip.IsPaused
                    ? FrameCache.GetFrame(clip.Animation, clip.Frame)
                    : FrameCache.GetInterpolatedFrame(clip.Animation, clip.Time);
            }
            finally
            {
                if (ignoreCache)
                {
                    FrameCache.PurgeCache();
                }
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
