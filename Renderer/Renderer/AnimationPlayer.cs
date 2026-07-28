using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Plays animations on a single skeleton: advances clip time, mixes the weighted clips into a
    /// frame and writes that frame's world-space pose. An <see cref="AnimationController"/> owns one
    /// player per skeleton its model can be animated from.
    /// </summary>
    public partial class AnimationPlayer
    {
        /// <summary>Gets the skeleton this player animates.</summary>
        public Skeleton Skeleton { get; }

        /// <summary>Gets the skeleton's world-space bind pose.</summary>
        public Matrix4x4[] BindPose { get; }

        /// <summary>Gets the world-space transform of each bone for the current animation frame.</summary>
        public Matrix4x4[] Pose { get; }

        /// <summary>Gets the currently active animation, or <see langword="null"/> if none is set.</summary>
        public Animation? ActiveAnimation => activeClip?.Animation;

        /// <summary>Gets the frame cache used to retrieve and interpolate animation frames.</summary>
        public AnimationFrameCache FrameCache { get; }

        /// <summary>Gets the decoded animation frame data for the current tick, or <see langword="null"/> when no animation is active.</summary>
        public Frame? AnimationFrame { get; private set; }

        private bool forceUpdate;

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
            get => activeClip?.Frame ?? 0;
            set
            {
                activeClip?.Frame = value;
                forceUpdate = true;
            }
        }

        /// <summary>Gets or sets the current playback time in seconds.</summary>
        public float Time
        {
            get => activeClip?.Time ?? 0f;
            set
            {
                activeClip?.Time = value;
                forceUpdate = true;
            }
        }

        /// <summary>
        /// Initializes a new <see cref="AnimationPlayer"/> for the given skeleton and flex controllers.
        /// The bind pose and pose buffers are supplied by the owner and may be shared with it.
        /// </summary>
        /// <param name="skeleton">The skeleton whose bones define the rig.</param>
        /// <param name="flexControllers">The flex controllers used for facial/morph animation.</param>
        /// <param name="bindPose">The skeleton's world-space bind pose, one entry per bone.</param>
        /// <param name="pose">The pose buffer to write into, one entry per bone.</param>
        public AnimationPlayer(Skeleton skeleton, FlexController[] flexControllers, Matrix4x4[] bindPose, Matrix4x4[] pose)
        {
            Skeleton = skeleton;
            BindPose = bindPose;
            Pose = pose;
            FrameCache = new(skeleton, flexControllers);
            BlendedFrame = new(skeleton, flexControllers);
        }

        /// <summary>
        /// Advances the animation by <paramref name="timeStep"/> seconds and writes the skeleton's
        /// world-space pose under <paramref name="rootTransform"/>.
        /// </summary>
        /// <param name="timeStep">Elapsed time in seconds since the last update.</param>
        /// <param name="rootTransform">The transform applied to the skeleton roots.</param>
        /// <returns><see langword="true"/> if the pose was updated; <see langword="false"/> if nothing changed.</returns>
        public bool Update(float timeStep, Matrix4x4 rootTransform)
        {
            if ((ActiveAnimation == null || IsPaused || ActiveAnimation.FrameCount == 1) && !forceUpdate)
            {
                return false;
            }

            if (!IsPaused && activeClip != null)
            {
                UpdateClips(timeStep);
            }

            AnimationFrame = GetFrame();
            forceUpdate = false;

            if (AnimationFrame == null)
            {
                BindPose.AsSpan().CopyTo(Pose);
                return true;
            }

            if (!IsUsingMixer && ActiveAnimation?.Clip is { IsAdditive: true })
            {
                // We need a frame we can write to without ruining the frame cache
                AnimationFrame.Bones.CopyTo(FrameCache.InterpolatedFrame.Bones);
                AnimationFrame = FrameCache.InterpolatedFrame;

                // Add over bind pose
                for (var i = 0; i < AnimationFrame.Bones.Length; i++)
                {
                    var bindPose = new FrameBone(Skeleton.Bones[i].Position, 1f, Skeleton.Bones[i].Angle);
                    AnimationFrame.Bones[i] = AnimationFrame.Bones[i].BlendAdd(bindPose, 1f);
                }
            }

            foreach (var root in Skeleton.Roots)
            {
                if (root.IsProceduralCloth)
                {
                    continue;
                }

                FramePose.ComputeWorldSubtree(root, rootTransform, AnimationFrame, Pose);
            }

            return true;
        }

        /// <summary>
        /// Sets the active animation with a blend-in time for smooth transitions.
        /// </summary>
        /// <param name="animation">The animation to activate, or <see langword="null"/> to clear.</param>
        /// <param name="blendTime">The time in seconds to blend from previous animations to the new animation.</param>
        /// <param name="looping">Whether the clip should loop when reaching the end.</param>
        public void SetAnimation(Animation? animation, float blendTime, bool looping)
        {
            FrameCache.PurgeCache();

            if (animation != null)
            {
                TransitionToClip(animation, blendTime, looping);
            }
            else
            {
                activeClip = null;
            }

            forceUpdate = true;
        }

        /// <summary>
        /// Returns the animation frame for the current time, using exact frame lookup when paused or interpolation during playback.
        /// </summary>
        /// <returns>The current animation frame, or <see langword="null"/> if no animation is active.</returns>
        public Frame? GetFrame() => GetBlendedFrame();
    }
}
