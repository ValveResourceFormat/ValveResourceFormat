using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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

            /// <summary>Gets or sets the blend transition time in seconds. Negative values indicate manual blending.</summary>
            public float BlendTime { get; set; }

            /// <summary>Gets or sets the bone mask name to apply per-bone weighting. Empty string means no mask.</summary>
            public string BoneMask { get; set; } = string.Empty;

            /// <summary>Gets or sets the playback speed multiplier for this clip.</summary>
            public float TimeScale { get; set; } = 1f;

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
        public bool ActiveClipFinished => activeClip != null && !activeClip.Looping && activeClip.IsPaused;

        private Clip? activeClip
        {
            get => CurrentSubController.HasValue ? CurrentSubController.Value.Handler.activeClip : field;
            set => field = value;
        }

        private Clip? previousClip;
        private readonly Dictionary<string, Clip> clips = [];
        private readonly Dictionary<string, ProceduralBoneRotationOverlay> proceduralBoneRotationOverlays = [];
        private readonly Frame BlendedFrame;
        private float currentBlendTime;

        private sealed class ProceduralBoneRotationOverlay(Dictionary<string, Quaternion> rotations, float duration)
        {
            public Dictionary<string, Quaternion> Rotations { get; } = rotations;
            public float Duration { get; } = duration;
            public float TimeRemaining { get; set; } = duration;
            public bool LoggedPoseDelta { get; set; }
        }

        /// <summary>
        /// Bone masks be used by clips to weigh transforms on a per-bone basis.
        /// </summary>
        public Dictionary<string, Half[]> BoneMaskDefinitions { get; } = [];

        /// <summary>
        /// Plays a short procedural local rotation overlay against named bones after normal animation has updated.
        /// </summary>
        public void PlayProceduralBoneRotationOverlay(string name, Dictionary<string, Quaternion> rotations, float duration)
        {
            if (rotations.Count == 0 || duration <= 0f)
            {
                return;
            }

            proceduralBoneRotationOverlays[name] = new(rotations, duration);
        }

        private void ApplyProceduralBoneRotationOverlays(float timeStep)
        {
            if (proceduralBoneRotationOverlays.Count == 0)
            {
                return;
            }

            var expired = new List<string>();
            foreach (var (name, overlay) in proceduralBoneRotationOverlays)
            {
                overlay.TimeRemaining -= timeStep;
                if (overlay.TimeRemaining <= 0f)
                {
                    expired.Add(name);
                    continue;
                }

                var progress = 1f - Math.Clamp(overlay.TimeRemaining / overlay.Duration, 0f, 1f);
                var weight = MathF.Sin(progress * MathF.PI);
                var beforePose = overlay.LoggedPoseDelta ? null : CaptureTrackedOverlayPose();
                foreach (var (boneName, rotation) in overlay.Rotations)
                {
                    ApplyLocalBoneRotationOffset(boneName, Quaternion.Slerp(Quaternion.Identity, rotation, weight));
                }

                if (!overlay.LoggedPoseDelta && progress >= 0.05f)
                {
                    overlay.LoggedPoseDelta = true;
                    LogProceduralOverlayPoseDelta(name, overlay, beforePose);
                }
            }

            foreach (var name in expired)
            {
                proceduralBoneRotationOverlays.Remove(name);
            }
        }

        private void ApplyLocalBoneRotationOffset(string boneName, Quaternion offset)
        {
            var bone = Skeleton[boneName];
            if (bone == null)
            {
                return;
            }

            var parentPose = bone.Parent != null ? Pose[bone.Parent.Index] : Transform;
            if (!Matrix4x4.Invert(parentPose, out var inverseParentPose))
            {
                return;
            }

            var localPose = Pose[bone.Index] * inverseParentPose;
            if (!Matrix4x4.Decompose(localPose, out var scale, out var rotation, out var translation))
            {
                return;
            }

            var adjustedLocalPose = Matrix4x4.CreateScale(scale)
                * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(rotation * offset))
                * Matrix4x4.CreateTranslation(translation);

            ApplyAdjustedLocalPoseRecursive(bone, adjustedLocalPose, parentPose);
        }

        private void ApplyAdjustedLocalPoseRecursive(Bone bone, Matrix4x4 localPose, Matrix4x4 parentPose)
        {
            var oldWorldPose = Pose[bone.Index];
            var newWorldPose = localPose * parentPose;
            Pose[bone.Index] = newWorldPose;

            if (!Matrix4x4.Invert(oldWorldPose, out var inverseOldWorldPose))
            {
                return;
            }

            foreach (var child in bone.Children)
            {
                var childLocalPose = Pose[child.Index] * inverseOldWorldPose;
                ApplyAdjustedLocalPoseRecursive(child, childLocalPose, newWorldPose);
            }
        }

        private Dictionary<string, Matrix4x4> CaptureTrackedOverlayPose()
        {
            var pose = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            foreach (var boneName in new[] { "hand_r", "hand_l", "arm_lower_r", "arm_lower_l" })
            {
                var bone = Skeleton[boneName];
                if (bone != null)
                {
                    pose[boneName] = Pose[bone.Index];
                }
            }

            return pose;
        }

        private void LogProceduralOverlayPoseDelta(
            string overlayName,
            ProceduralBoneRotationOverlay overlay,
            Dictionary<string, Matrix4x4>? beforePose)
        {
            if (beforePose == null || beforePose.Count == 0)
            {
                WriteAgentDebug(
                    "WATCHDOG_FAIL,H13",
                    "Renderer/Renderer/AnimationController.Mixer.cs:ApplyProceduralBoneRotationOverlays",
                    "WATCHDOG_FAIL pose issue: procedural overlay tracked bones missing",
                    new
                    {
                        overlayName,
                        requestedBones = overlay.Rotations.Keys.Order().ToArray(),
                    });
                return;
            }

            var deltas = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var (boneName, before) in beforePose)
            {
                var bone = Skeleton[boneName];
                if (bone != null)
                {
                    deltas[boneName] = MatrixDelta(before, Pose[bone.Index]);
                }
            }

            var maxDelta = deltas.Count == 0 ? 0f : deltas.Values.Max();
            var failed = maxDelta < 0.0001f;
            WriteAgentDebug(
                failed ? "WATCHDOG_FAIL,H13" : "H13",
                "Renderer/Renderer/AnimationController.Mixer.cs:ApplyProceduralBoneRotationOverlays",
                failed
                    ? "WATCHDOG_FAIL pose issue: procedural overlay did not move tracked bones"
                    : "procedural overlay moved tracked bones",
                new
                {
                    overlayName,
                    requestedBones = overlay.Rotations.Keys.Order().ToArray(),
                    trackedDeltas = deltas,
                    maxDelta,
                });
        }

        private static float MatrixDelta(Matrix4x4 a, Matrix4x4 b)
            => MathF.Abs(a.M11 - b.M11)
                + MathF.Abs(a.M12 - b.M12)
                + MathF.Abs(a.M13 - b.M13)
                + MathF.Abs(a.M14 - b.M14)
                + MathF.Abs(a.M21 - b.M21)
                + MathF.Abs(a.M22 - b.M22)
                + MathF.Abs(a.M23 - b.M23)
                + MathF.Abs(a.M24 - b.M24)
                + MathF.Abs(a.M31 - b.M31)
                + MathF.Abs(a.M32 - b.M32)
                + MathF.Abs(a.M33 - b.M33)
                + MathF.Abs(a.M34 - b.M34)
                + MathF.Abs(a.M41 - b.M41)
                + MathF.Abs(a.M42 - b.M42)
                + MathF.Abs(a.M43 - b.M43)
                + MathF.Abs(a.M44 - b.M44);

        private static void WriteAgentDebug(string hypothesisId, string location, string message, object data)
        {
            try
            {
                var payload = new
                {
                    sessionId = "0ef808",
                    runId = "watchdog",
                    hypothesisId,
                    location,
                    message,
                    data,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                };
                File.AppendAllText(
                    @"c:\Users\ayden\Documents\Github Projects\cs2 viewer\ValveResourceFormat\debug-0ef808.log",
                    JsonSerializer.Serialize(payload) + Environment.NewLine);
            }
            catch
            {
                // Debug instrumentation must not affect animation.
            }
        }

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
                    clip.Time += timeStep * clip.TimeScale;

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
        /// <returns>The current animation frame, or <see langword="null"/> if no animation is active.</returns>
        private Frame? GetBlendedFrame()
        {
            IsUsingMixer = false;

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

            IsUsingMixer = true;
            BlendedFrame.FrameIndex = -1;
            BlendedFrame.Datas.AsSpan().Clear();
            for (var i = 0; i < BlendedFrame.Bones.Length; i++)
            {
                var bindPose = Skeleton.Bones[i];
                BlendedFrame.Bones[i] = new FrameBone(bindPose.Position, 1f, bindPose.Angle);
            }

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
                var isAdditive = animation.Clip?.IsAdditive == true;
                newClip = new Clip(animation) { Looping = Looping, BlendTime = blendTime, IsAdditive = isAdditive };
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
        /// <param name="restartIfNew">Whether to restart the animation if its just now fading in.</param>
        public void SetAnimationWeight(string name, float weight, bool restartIfNew = false)
        {
            if (clips.TryGetValue(name, out var clip))
            {
                var wasZero = clip.Weight == 0f;
                clip.Weight = weight;

                if (restartIfNew && wasZero && weight > 0f)
                {
                    clip.Time = 0f;
                    clip.IsPaused = false;
                }
            }

            if (CurrentSubController is { } subController)
            {
                subController.Handler.SetAnimationWeight(name, weight, restartIfNew);
            }
        }

        /// <summary>
        /// Gets whether a non-looping clip has finished playback and is paused at its last frame.
        /// </summary>
        /// <param name="name">The name of the animation clip.</param>
        public bool IsClipPausedAtEnd(string name)
        {
            if (CurrentSubController is { } subController)
            {
                return subController.Handler.IsClipPausedAtEnd(name);
            }

            if (!clips.TryGetValue(name, out var clip) || clip.Looping || clip.Animation.FrameCount <= 1)
            {
                return false;
            }

            var maxTime = (clip.Animation.FrameCount - 1) / clip.Animation.Fps;
            return clip.IsPaused && clip.Time >= maxTime - 1e-4f;
        }

        /// <summary>
        /// Gets the maximum playback time in seconds for a registered clip.
        /// </summary>
        /// <param name="name">The name of the animation clip.</param>
        public float? GetClipMaxTime(string name)
        {
            if (CurrentSubController is { } subController)
            {
                return subController.Handler.GetClipMaxTime(name);
            }

            if (!clips.TryGetValue(name, out var clip) || clip.Animation.FrameCount <= 1)
            {
                return null;
            }

            return (clip.Animation.FrameCount - 1) / clip.Animation.Fps;
        }

        /// <summary>
        /// Gets the current playback time in seconds for a registered clip.
        /// </summary>
        /// <param name="name">The name of the animation clip.</param>
        public float? GetClipTime(string name)
        {
            if (CurrentSubController is { } subController)
            {
                return subController.Handler.GetClipTime(name);
            }

            if (!clips.TryGetValue(name, out var clip))
            {
                return null;
            }

            return clip.Time;
        }

        /// <summary>
        /// Gets whether a non-looping clip has reached its final frame.
        /// </summary>
        /// <param name="name">The name of the animation clip.</param>
        public bool IsClipFinished(string name)
        {
            if (IsClipPausedAtEnd(name))
            {
                return true;
            }

            var maxTime = GetClipMaxTime(name);
            var time = GetClipTime(name);

            return maxTime.HasValue && time.HasValue && time.Value >= maxTime.Value - 1e-4f;
        }

        /// <summary>
        /// Registers a clip in the mixer without changing the active animation.
        /// </summary>
        /// <param name="animation">The animation to register.</param>
        /// <param name="looping">Whether the clip should loop by default.</param>
        public bool EnsureClipRegistered(Animation animation, bool looping = false)
        {
            if (animation == null)
            {
                return false;
            }

            if (animation.Clip is { } nmClip
                && ExternalSkeletons.TryGetValue(nmClip.SkeletonName, out var subController))
            {
                return subController.Handler.EnsureClipRegistered(animation, looping);
            }

            if (animation.Clip is { } animationClip
                && !string.Equals(Skeleton.Name, animationClip.SkeletonName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var animName = animation.Name;
            if (!clips.TryGetValue(animName, out var clip))
            {
                clip = new Clip(animation)
                {
                    Looping = looping,
                    IsAdditive = animation.Clip?.IsAdditive == true,
                };
                clips[animName] = clip;
            }

            return true;
        }

        /// <summary>
        /// Gets whether an animation can drive this controller's rendered skeleton.
        /// </summary>
        /// <param name="animation">The animation to test.</param>
        public bool CanDriveAnimation(Animation animation)
            => animation.Clip == null
                || ExternalSkeletons.ContainsKey(animation.Clip.SkeletonName)
                || string.Equals(Skeleton.Name, animation.Clip.SkeletonName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the registered clip name with the highest blend weight.
        /// </summary>
        public string? GetDominantClipName()
        {
            if (CurrentSubController is { } subController)
            {
                return subController.Handler.GetDominantClipName();
            }

            string? dominantClip = activeClip?.Animation.Name;
            var dominantWeight = activeClip?.Weight ?? 0f;

            foreach (var clip in clips.Values)
            {
                var shouldPreferClip =
                    clip.Weight > dominantWeight
                    || (clip.Weight >= dominantWeight && clip != activeClip && !clip.Looping);

                if (!shouldPreferClip)
                {
                    continue;
                }

                dominantWeight = clip.Weight;
                dominantClip = clip.Animation.Name;
            }

            return dominantClip;
        }

        /// <summary>
        /// Resumes playback for a clip and optionally sets looping and time.
        /// </summary>
        /// <param name="name">The name of the animation clip.</param>
        /// <param name="looping">Whether the clip should loop.</param>
        /// <param name="time">Optional playback time to set.</param>
        public void ResumeClip(string name, bool looping, float? time = null)
        {
            if (CurrentSubController is { } subController)
            {
                subController.Handler.ResumeClip(name, looping, time);
                return;
            }

            if (!clips.TryGetValue(name, out var clip))
            {
                return;
            }

            clip.Looping = looping;
            if (time.HasValue)
            {
                clip.Time = time.Value;
            }

            clip.IsPaused = false;
        }

        /// <summary>
        /// Sets properties for a clip with the specified animation name.
        /// </summary>
        /// <param name="name">The name of the animation.</param>
        /// <param name="time">Optional playback time to set.</param>
        /// <param name="looping">Optional looping flag to set.</param>
        /// <param name="boneMask">Optional bone mask name to set.</param>
        /// <param name="timeScale">Optional playback speed multiplier to set.</param>
        public void SetAnimationProperties(string name, float? time = null, bool? looping = null, string? boneMask = null, float? timeScale = null)
        {
            if (clips.TryGetValue(name, out var clip))
            {
                if (time.HasValue)
                {
                    clip.Time = time.Value;
                    clip.IsPaused = false;
                }

                if (looping.HasValue)
                {
                    clip.Looping = looping.Value;
                }

                if (boneMask != null)
                {
                    clip.BoneMask = boneMask;
                }

                if (timeScale.HasValue)
                {
                    clip.TimeScale = timeScale.Value;
                }
            }

            if (CurrentSubController is { } subController)
            {
                subController.Handler.SetAnimationProperties(name, time, looping, boneMask, timeScale);
            }
        }
    }
}
