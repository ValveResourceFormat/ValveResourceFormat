using System.Diagnostics;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Manages skeletal animation playback and computes animated bone poses.
    /// </summary>
    public partial class AnimationController
    {
        /// <summary>Represents an animation clip with its playback state.</summary>
        public record class Clip(Animation Animation)
        {
            /// <summary>Gets or sets the current playback time in seconds.</summary>
            public float Time { get; set; }

            /// <summary>Gets or sets whether this clip should blend additively with other animations.</summary>
            public bool IsAdditive { get; set; }

            /// <summary>Gets or sets whether playback is paused.</summary>
            public bool IsPaused { get; set; }

            /// <summary>Gets or sets whether the clip should loop when reaching the end.</summary>
            public bool Looping { get; set; } = true;

            /// <summary>Gets or sets the blend weight (0.0 to 1.0) for this clip.</summary>
            public float Weight { get; set; } = 1f;

            /// <summary>Gets or sets the blend transition time in seconds. A value of -1 indicates manual blending.</summary>
            public float BlendTime { get; set; }

            /// <summary>Gets or sets the bone mask name to apply per-bone weighting. Empty string means no mask.</summary>
            public string BoneMask { get; set; } = string.Empty;

            /// <summary>Gets whether this clip uses time-based transition blending.</summary>
            public bool IsTimeBasedTransition => BlendTime > 0f;

            /// <summary>Gets whether this clip uses manual weight blending.</summary>
            public bool IsManualBlend => BlendTime == -1;

            /// <summary>Gets or sets the current frame index.</summary>
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

        /// <summary>
        /// Gets whether the active animation clip has finished playing (is not looping and has reached the end).
        /// </summary>
        public bool ActiveClipFinished => runner.ActiveClipFinished;

        /// <summary>
        /// Gets the current clips.
        /// </summary>
        public Dictionary<string, Clip> Clips => clips;

        private Clip? activeClip;
        private Clip? previousClip;
        private readonly Dictionary<string, Clip> clips = [];
        private readonly Frame BlendedFrame;

        // Scratch for composing additive frames over the bind pose without mutating cached frames.
        private readonly Frame AdditiveFrame;
        private float currentBlendTime;

        /// <summary>
        /// Bone masks are used by clips to weigh transforms on a per-bone basis.
        /// </summary>
        public Dictionary<string, Half[]> BoneMaskDefinitions { get; } = [];

        /// <summary>
        /// Registers a bone mask for per-bone transform weighting.
        /// </summary>
        /// <param name="name">The name of the bone mask.</param>
        /// <param name="boneWeights">Dictionary mapping bone names to weight values (0.0 to 1.0).</param>
        /// <param name="skeletonName">Optional skeleton name to pass to subcontroller.</param>
        public void RegisterBoneMask(string name, Dictionary<string, float> boneWeights, string? skeletonName = null)
        {
            if (skeletonName != null && ExternalSkeletons.TryGetValue(skeletonName, out var subController))
            {
                subController.Handler.RegisterBoneMask(name, boneWeights);
                return;
            }

            var maskArray = new Half[Skeleton.Bones.Length];

            foreach (var (boneName, weight) in boneWeights)
            {
                var boneIndex = Skeleton.GetBoneIndex(boneName);
                if (boneIndex != -1)
                {
                    maskArray[boneIndex] = (Half)weight;
                }
            }

            BoneMaskDefinitions[name] = maskArray;
        }

        /// <summary>
        /// Updates time and weights for all active clips during playback.
        /// </summary>
        /// <param name="timeStep">Elapsed time in seconds since the last update.</param>
        private void UpdateClips(float timeStep)
        {
            if (activeClip == null)
            {
                return;
            }

            var allPaused = true;
            foreach (var clip in clips.Values)
            {
                if (!clip.IsPaused)
                {
                    allPaused = false;
                    break;
                }
            }

            IsPaused = allPaused;

            // Update time for all clips
            foreach (var clip in clips.Values)
            {
                if (!clip.IsPaused && clip.Animation.FrameCount > 1)
                {
                    clip.Time += timeStep;

                    if (!clip.Looping)
                    {
                        var lastFrame = clip.Animation!.FrameCount - 1;
                        var maxTime = lastFrame / clip.Animation.Fps;

                        if (clip.Time > maxTime)
                        {
                            clip.IsPaused = true;
                            clip.Frame = lastFrame;
                        }
                    }
                }
            }

            if (activeClip.IsTimeBasedTransition && previousClip != null)
            {
                // Distribute blend weights between previous clip and active clip only.
                currentBlendTime -= timeStep;

                if (currentBlendTime <= 0f)
                {
                    previousClip.Weight = 0f;
                    activeClip.Weight = 1f;
                    previousClip = null;
                }
                else
                {
                    var t = activeClip.BlendTime > 0f
                        ? 1f - Math.Clamp(currentBlendTime / activeClip.BlendTime, 0f, 1f)
                        : 1f;

                    var blendProgress = t * t * (3f - 2f * t);

                    activeClip.Weight = blendProgress;
                    previousClip.Weight = 1f - blendProgress;

                    foreach (var clip in clips.Values)
                    {
                        if (clip != activeClip && clip != previousClip)
                        {
                            clip.Weight = 0f;
                        }
                    }
                }

                var sum = 0f;
                foreach (var clip in clips.Values)
                {
                    sum += clip.Weight;
                }
                Debug.Assert(sum > 0f, "Total blend weight should be greater than zero.");
                Debug.Assert(Math.Abs(sum - 1f) < 0.01f, $"Total blend weight should be approximately 1. Found: {sum}");
            }
        }

        /// <summary>
        /// Gets whether the current animation frame is the result of blending multiple clips together.
        /// </summary>
        public bool IsUsingMixer { get; private set; }

        /// <summary>
        /// Returns the animation frame for the current time, blending multiple clips if needed.
        /// </summary>
        /// <param name="usingMixer">Whether the returned frame is a blend of multiple clips.</param>
        /// <returns>The current animation frame, or <see langword="null"/> if no animation is active.</returns>
        private Frame? GetBlendedFrame(out bool usingMixer)
        {
            usingMixer = false;

            if (activeClip == null)
            {
                return null;
            }

            // Check if blending is needed
            var needsBlending = false;
            foreach (var clip in clips.Values)
            {
                if (clip != activeClip && clip.Weight > 0f)
                {
                    needsBlending = true;
                    break;
                }
            }

            if (!needsBlending)
            {
                return SampleFrame(activeClip);
            }

            usingMixer = true;
            BlendedFrame.FrameIndex = -1;
            BlendedFrame.Bones.AsSpan().Clear();
            BlendedFrame.Datas.AsSpan().Clear();

            var totalWeight = 0f;
            foreach (var clip in clips.Values)
            {
                if (clip.Weight <= 0f)
                {
                    continue;
                }

                var frame = SampleFrame(clip);
                var blendFactor = clip.IsAdditive
                    ? clip.Weight
                    : clip.Weight / (totalWeight + clip.Weight);

                // Apply bone mask if specified
                Half[]? boneMask = null;
                if (!string.IsNullOrEmpty(clip.BoneMask))
                {
                    BoneMaskDefinitions.TryGetValue(clip.BoneMask, out boneMask);
                }

                for (var i = 0; i < frame.Bones.Length; i++)
                {
                    var boneMaskWeight = boneMask != null ? (float)boneMask[i] : 1f;
                    var weightedBlendFactor = blendFactor * boneMaskWeight;

                    BlendedFrame.Bones[i] = clip.IsAdditive
                        ? BlendedFrame.Bones[i].BlendAdd(frame.Bones[i], weightedBlendFactor)
                        : BlendedFrame.Bones[i].Blend(frame.Bones[i], weightedBlendFactor);
                }

                for (var i = 0; i < frame.Datas.Length; i++)
                {
                    BlendedFrame.Datas[i] = clip.IsAdditive
                        ? BlendedFrame.Datas[i] + frame.Datas[i] * blendFactor
                        : float.Lerp(BlendedFrame.Datas[i], frame.Datas[i], blendFactor);
                }

                totalWeight += clip.Weight;
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
        /// Transitions to a new animation clip with the specified blend time, managing clip weights appropriately.
        /// </summary>
        /// <param name="animation">The animation to transition to.</param>
        /// <param name="blendTime">The blend time in seconds. 0 for instant transition, -1 for manual blending.</param>
        private void TransitionToClip(Animation animation, float blendTime)
        {
            var animName = animation.Name;

            // Check if clip already exists
            if (!clips.TryGetValue(animName, out var newClip))
            {
                newClip = new Clip(animation) { Looping = Looping, BlendTime = blendTime, IsAdditive = animation.SupportsMixerAdditive };
                clips[animName] = newClip;
            }
            else
            {
                // Update existing clip properties
                newClip.Looping = Looping;
                newClip.BlendTime = blendTime;

                newClip.IsPaused = false;
                newClip.Frame = 0;
            }

            // Handle blending
            if (activeClip == newClip)
            {
                // Re-setting the same animation should not create a self-blend transition.
                previousClip = null;

                foreach (var clip in clips.Values)
                {
                    clip.Weight = 0f;
                }

                newClip.Weight = 1f;

                if (blendTime == 0f)
                {
                    FrameCache.Clear();
                }
            }
            else if (blendTime > 0f && activeClip != null)
            {
                // Time-based transition: only blend from previous clip -> active clip.
                previousClip = activeClip;
                previousClip.Weight = 1f;

                // Set all other clips to zero immediately.
                foreach (var clip in clips.Values)
                {
                    if (clip != previousClip && clip != newClip)
                    {
                        clip.Weight = 0f;
                    }
                }

                newClip.Weight = 0f;
                currentBlendTime = blendTime;
            }
            else if (blendTime == -1f && activeClip != null)
            {
                // Manual blend: keep previous clip, user may set weights manually.
                previousClip = activeClip;
                previousClip.Weight = 1f;

                foreach (var clip in clips.Values)
                {
                    if (clip != previousClip && clip != newClip)
                    {
                        clip.Weight = 0f;
                    }
                }

                newClip.Weight = 0f;
            }
            else
            {
                // No blending - disable previous clip and all other clips.
                previousClip = null;

                foreach (var clip in clips.Values)
                {
                    clip.Weight = 0f;
                }

                newClip.Weight = 1f;

                if (blendTime == 0f)
                {
                    FrameCache.Clear();
                }
            }

            activeClip = newClip;
        }

        /// <summary>
        /// Sets the blend weight for a clip with the specified animation name.
        /// </summary>
        /// <param name="name">The name of the animation.</param>
        /// <param name="weight">The weight value (0.0 to 1.0).</param>
        /// <param name="restartIfNew">Whether to restart the animation if it's just now fading in.</param>
        public void SetAnimationWeight(string name, float weight, bool restartIfNew = false)
            => runner.SetAnimationWeight(name, weight, restartIfNew);

        /// <summary>
        /// Sets properties for a clip with the specified animation name.
        /// </summary>
        /// <param name="name">The name of the animation.</param>
        /// <param name="time">Optional playback time to set.</param>
        /// <param name="looping">Optional looping flag to set.</param>
        /// <param name="boneMask">Optional bone mask name to set.</param>
        public void SetAnimationProperties(string name, float? time = null, bool? looping = null, string? boneMask = null)
            => runner.SetAnimationProperties(name, time, looping, boneMask);
    }
}
