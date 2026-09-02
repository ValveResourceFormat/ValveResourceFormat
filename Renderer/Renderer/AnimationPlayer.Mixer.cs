using System.Diagnostics;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Blends the weighted animation clips playing on one skeleton into a single frame.
    /// </summary>
    public partial class AnimationPlayer
    {
        /// <summary>Represents an animation clip with its playback state.</summary>
        public record class PlaybackClip(Animation Animation)
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

            /// <summary>Gets or sets how playback transitions into this clip.</summary>
            public ClipTransition Transition { get; set; }

            /// <summary>Gets or sets how long a <see cref="ClipTransition.Crossfade"/> takes, in seconds.</summary>
            public float BlendDuration { get; set; }

            /// <summary>Gets or sets the mask scoping this clip per bone and per flex controller. Empty for no mask.</summary>
            public string MaskName { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets what recomputes this clip's <see cref="Weight"/> and <see cref="Time"/> from
            /// another clip's cycle. Null for a clip that plays on its own.
            /// </summary>
            public ClipDriver? Driver { get; set; }

            /// <summary>Gets whether another clip drives this one.</summary>
            public bool IsDriven => Driver != null;

            /// <summary>Gets or sets the current frame index within the cycle being played.</summary>
            public int Frame
            {
                get => Animation.GetNearestFrame(Time);
                set => Time = Animation.Fps != 0 ? value / Animation.Fps : 0f;
            }
        }

        /// <summary>How playback moves into a clip.</summary>
        public enum ClipTransition
        {
            /// <summary>The clip takes over at full weight immediately.</summary>
            Instant = 0,

            /// <summary>The clip fades in over <see cref="PlaybackClip.BlendDuration"/> seconds.</summary>
            Crossfade = 1,

            /// <summary>The clip starts at zero weight and the caller sets its weight itself.</summary>
            Manual = 2,
        }

        /// <summary>
        /// Takes a driven clip's playback position from an owner clip's cycle, and its weight from that owner.
        /// </summary>
        /// <param name="Owner">The clip whose cycle position drives this one.</param>
        public abstract record ClipDriver(PlaybackClip Owner)
        {
            /// <summary>The weight the driven clip contributes, before the owner's own weight.</summary>
            public abstract float EvaluateWeight(AnimationPlayer player);
        }

        /// <summary>Drives one of a sequence's auto layers from its blend curve at the owner's cycle.</summary>
        public sealed record AutoLayerDriver(PlaybackClip Owner, AnimationAutoLayer Layer) : ClipDriver(Owner)
        {
            /// <inheritdoc/>
            public override float EvaluateWeight(AnimationPlayer player)
                => EvaluateAutoLayerWeight(Layer, Owner.Animation.GetCycleFraction(Owner.Time));
        }

        /// <summary>Drives one entry of a blend sequence's fetch from the live pose parameter values.</summary>
        public sealed record BlendDriver(PlaybackClip Owner, SequenceAnimation Sequence, int Index) : ClipDriver(Owner)
        {
            /// <inheritdoc/>
            public override float EvaluateWeight(AnimationPlayer player)
                => Sequence.Fetch!.Value.GetBlendWeight(Index, player.GetBlendPoseValue(Sequence, 0), player.GetBlendPoseValue(Sequence, 1));
        }

        /// <summary>
        /// Gets whether the active animation clip has finished playing (is not looping and has reached the end).
        /// </summary>
        public bool ActiveClipFinished => activeClip != null && !activeClip.Looping && activeClip.IsPaused;

        /// <summary>Gets the current clips. Use <see cref="ClearClips"/> to empty them.</summary>
        public IReadOnlyDictionary<string, PlaybackClip> Clips => clips;

        private PlaybackClip? activeClip;
        private PlaybackClip? previousClip;
        private readonly Dictionary<string, PlaybackClip> clips = [];
        private const string WarpSuffix = ".warp";
        private readonly Frame BlendedFrame;
        private float currentBlendTime;

        /// <summary>
        /// One named mask over the bones a clip may move and the flex controllers it may drive. Null on
        /// either side is unrestricted.
        /// </summary>
        private sealed class ClipMask
        {
            public Half[]? BoneWeights { get; set; }
            public float[]? MorphWeights { get; set; }
        }

        private readonly Dictionary<string, ClipMask> masks = [];

        /// <summary>Optional resolver from an animation or sequence name to the loaded <see cref="Animation"/>.</summary>
        public Func<string, Animation?>? AnimationLookup { get; set; }

        private readonly Dictionary<string, PoseParameter> poseParameterDefinitions = [];
        private readonly Dictionary<string, float> poseParameterValues = [];

        /// <summary>Clears all clips and blend state.</summary>
        public void ClearClips()
        {
            activeClip = null;
            previousClip = null;
            clips.Clear();
        }

        /// <summary>
        /// Registers a pose parameter a blend sequence positions its animations along, with its live
        /// value at zero clamped into range.
        /// </summary>
        public void RegisterPoseParameter(PoseParameter parameter)
        {
            poseParameterDefinitions[parameter.Name] = parameter;
            poseParameterValues[parameter.Name] = parameter.Clamp(0f);
        }

        /// <summary>
        /// Sets the live value of a pose parameter, clamped to its range when it was registered, and
        /// forces the next <see cref="Update"/> to recompute the pose.
        /// </summary>
        public void SetPoseParameter(string name, float value)
        {
            poseParameterValues[name] = poseParameterDefinitions.TryGetValue(name, out var parameter)
                ? parameter.Clamp(value)
                : value;

            forceUpdate = true;
        }

        /// <summary>
        /// Gets the live value of a pose parameter, or zero for one that was never registered or set.
        /// </summary>
        public float GetPoseParameter(string name)
            => string.IsNullOrEmpty(name) ? 0f : poseParameterValues.GetValueOrDefault(name);

        /// <summary>
        /// Registers a bone mask for per-bone transform weighting.
        /// </summary>
        /// <param name="name">The name of the bone mask.</param>
        /// <param name="boneWeights">Dictionary mapping bone names to weight values (0.0 to 1.0).</param>
        public void RegisterBoneMask(string name, Dictionary<string, float> boneWeights)
        {
            var maskArray = new Half[Skeleton.Bones.Length];

            foreach (var (boneName, weight) in boneWeights)
            {
                var boneIndex = Skeleton.GetBoneIndex(boneName);
                if (boneIndex != -1)
                {
                    maskArray[boneIndex] = (Half)weight;
                }
            }

            GetOrAddMask(name).BoneWeights = maskArray;
        }

        /// <summary>
        /// Registers a morph mask for per-flex-controller weighting. A controller not named defaults to 1.
        /// </summary>
        /// <param name="name">The name of the morph mask.</param>
        /// <param name="controllerWeights">Dictionary mapping flex controller names to weight values.</param>
        public void RegisterMorphMask(string name, Dictionary<string, float> controllerWeights)
        {
            var flexControllers = FrameCache.FlexControllers;
            var maskArray = new float[flexControllers.Length];
            Array.Fill(maskArray, 1f);

            foreach (var (controllerName, weight) in controllerWeights)
            {
                var index = Array.FindIndex(flexControllers, c => c.Name.Equals(controllerName, StringComparison.OrdinalIgnoreCase));
                if (index != -1)
                {
                    maskArray[index] = weight;
                }
            }

            GetOrAddMask(name).MorphWeights = maskArray;
        }

        private ClipMask GetOrAddMask(string name)
        {
            if (!masks.TryGetValue(name, out var mask))
            {
                mask = new ClipMask();
                masks[name] = mask;
            }

            return mask;
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

            foreach (var clip in clips.Values)
            {
                if (clip.IsDriven)
                {
                    continue;
                }

                if (!clip.IsPaused && clip.Animation.FrameCount > 1)
                {
                    var previousTime = clip.Time;
                    clip.Time += timeStep;

                    var finished = false;

                    if (!clip.Looping)
                    {
                        var lastFrame = clip.Animation!.FrameCount - 1;
                        var maxTime = lastFrame / clip.Animation.Fps;

                        if (clip.Time > maxTime)
                        {
                            clip.IsPaused = true;

                            // Clamping the overshoot also keeps the event sampling below from wrapping
                            // around and firing the events at the start of the clip again
                            clip.Frame = lastFrame;
                            finished = true;
                        }
                    }

                    SampleEvents(clip, previousTime, clip.Time, finished);
                }
            }

            var allPaused = true;
            foreach (var clip in clips.Values)
            {
                if (!clip.IsDriven && !clip.IsPaused)
                {
                    allPaused = false;
                    break;
                }
            }

            IsPaused = allPaused;

            UpdateActiveClipSounds();

            if (activeClip.Transition == ClipTransition.Crossfade && previousClip != null)
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
                    var t = activeClip.BlendDuration > 0f
                        ? 1f - Math.Clamp(currentBlendTime / activeClip.BlendDuration, 0f, 1f)
                        : 1f;

                    var blendProgress = t * t * (3f - 2f * t);

                    activeClip.Weight = blendProgress;
                    previousClip.Weight = 1f - blendProgress;

                    ZeroWeightsExcept(activeClip, previousClip);
                }

                var sum = 0f;
                foreach (var clip in clips.Values)
                {
                    sum += clip.Weight;
                }
                Debug.Assert(sum > 0f, "Total blend weight should be greater than zero.");
                Debug.Assert(Math.Abs(sum - 1f) < 0.01f, $"Total blend weight should be approximately 1. Found: {sum}");
            }

            UpdateDrivenClips();
        }

        /// <summary>
        /// Recomputes every driven clip's playback time and blend weight from its owner clip's cycle position.
        /// </summary>
        private void UpdateDrivenClips()
        {
            foreach (var clip in clips.Values)
            {
                if (clip.Driver is not { } driver)
                {
                    continue;
                }

                clip.Weight = driver.EvaluateWeight(this) * driver.Owner.Weight;
                clip.Time = driver.Owner.Animation.GetCycleFraction(driver.Owner.Time) * clip.Animation.CycleDuration;
            }
        }

        /// <summary>
        /// The pose parameter value driving one dimension of a blend, or zero when it names none.
        /// </summary>
        private float GetBlendPoseValue(SequenceAnimation sequence, int dimension)
        {
            var name = dimension < sequence.PoseParameterNames.Length ? sequence.PoseParameterNames[dimension] : string.Empty;
            return GetPoseParameter(name);
        }

        /// <summary>
        /// Evaluates an auto layer's blend curve at a point in its owner's cycle: a trapezoid rising from
        /// Start to Peak and falling from Tail to End, at full weight throughout when Start equals End.
        /// </summary>
        private static float EvaluateAutoLayerWeight(AnimationAutoLayer layer, float cycle)
        {
            if (layer.Start == layer.End)
            {
                return 1f;
            }

            if (layer.NoBlend)
            {
                return cycle >= layer.Start && cycle <= layer.End ? 1f : 0f;
            }

            var rising = layer.Start != layer.Peak ? (cycle - layer.Start) / (layer.Peak - layer.Start) : 1f;
            var falling = layer.Tail != layer.End ? (layer.End - cycle) / (layer.End - layer.Tail) : 1f;

            var weight = Math.Clamp(Math.Min(rising, falling), 0f, 1f);

            if (layer.Spline)
            {
                weight = weight * weight * (3f - 2f * weight);
            }

            return weight;
        }

        /// <summary>
        /// Adds a clip for each of <paramref name="sequence"/>'s auto layers whose target resolves through
        /// <see cref="AnimationLookup"/>, keyed off <paramref name="ownerKey"/>. Pose driven layers are skipped.
        /// </summary>
        private void CreateAutoLayerClips(string ownerKey, PlaybackClip owner, SequenceAnimation sequence)
        {
            for (var i = 0; i < sequence.AutoLayers.Length; i++)
            {
                var layer = sequence.AutoLayers[i];

                if (layer.Pose || Resolve(layer.ReferencedAnimationName) is not { } referenced)
                {
                    continue;
                }

                var key = $"{ownerKey}$autolayer{i}";
                var layerClip = DriveClip(key, referenced, new AutoLayerDriver(owner, layer));

                layerClip.IsAdditive = layer.Subtract || referenced.IsAdditive;
                layerClip.MaskName = referenced is SequenceAnimation referencedSequence ? referencedSequence.BoneMaskName : string.Empty;

                if (referenced is SequenceAnimation { IsBlend: true } layerBlend)
                {
                    CreateBlendReferenceClips(key, layerClip, layerBlend);
                }
            }
        }

        /// <summary>
        /// Adds a clip for each entry of <paramref name="sequence"/>'s blend fetch that resolves through
        /// <see cref="AnimationLookup"/>, keyed off <paramref name="ownerKey"/>. Additivity and mask come
        /// from the blend sequence itself, not from the referenced animations.
        /// </summary>
        private void CreateBlendReferenceClips(string ownerKey, PlaybackClip owner, SequenceAnimation sequence)
        {
            var referenceNames = sequence.BlendReferenceNames;

            for (var i = 0; i < referenceNames.Length; i++)
            {
                if (Resolve(referenceNames[i]) is not { } referenced)
                {
                    continue;
                }

                var referenceClip = DriveClip($"{ownerKey}$blend{i}", referenced, new BlendDriver(owner, sequence, i));

                referenceClip.IsAdditive = sequence.IsAdditive;
                referenceClip.MaskName = sequence.BoneMaskName;
            }
        }

        private Animation? Resolve(string name)
            => string.IsNullOrEmpty(name) ? null : AnimationLookup?.Invoke(name);

        /// <summary>Gets or adds the clip at <paramref name="key"/>, driven by <paramref name="driver"/>.</summary>
        private PlaybackClip DriveClip(string key, Animation animation, ClipDriver driver)
        {
            if (!clips.TryGetValue(key, out var clip))
            {
                clip = new PlaybackClip(animation) { Looping = true };
                clips[key] = clip;
            }

            clip.Driver = driver;

            return clip;
        }

        /// <summary>Whether the last frame produced was a blend of several clips rather than one sampled clip.</summary>
        internal bool IsUsingMixer { get; private set; }

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
            BlendedFrame.Clear(Skeleton);

            var totalWeight = 0f;
            foreach (var clip in clips.Values)
            {
                if (clip.Weight <= 0f)
                {
                    continue;
                }

                if (clip.Animation is SequenceAnimation { IsBlend: true })
                {
                    // A blend sequence carries no frame data of its own; its reference clips are sampled instead.
                    continue;
                }

                var frame = SampleFrame(clip);
                var blendFactor = clip.IsAdditive
                    ? clip.Weight
                    : clip.Weight / (totalWeight + clip.Weight);

                var mask = string.IsNullOrEmpty(clip.MaskName) ? null : masks.GetValueOrDefault(clip.MaskName);
                var boneMask = mask?.BoneWeights;

                for (var i = 0; i < frame.Bones.Length; i++)
                {
                    var boneMaskWeight = boneMask != null ? (float)boneMask[i] : 1f;
                    var weightedBlendFactor = blendFactor * boneMaskWeight;

                    BlendedFrame.Bones[i] = clip.IsAdditive
                        ? BlendedFrame.Bones[i].BlendAdd(clip.Animation.GetAdditiveDelta(i, frame.Bones[i]), weightedBlendFactor)
                        : BlendedFrame.Bones[i].Blend(frame.Bones[i], weightedBlendFactor);
                }

                var morphMask = mask?.MorphWeights;

                for (var i = 0; i < frame.Datas.Length; i++)
                {
                    var morphMaskWeight = morphMask != null ? morphMask[i] : 1f;
                    var weightedDataBlendFactor = blendFactor * morphMaskWeight;

                    BlendedFrame.Datas[i] = clip.IsAdditive
                        ? BlendedFrame.Datas[i] + frame.Datas[i] * weightedDataBlendFactor
                        : float.Lerp(BlendedFrame.Datas[i], frame.Datas[i], weightedDataBlendFactor);
                }

                totalWeight += clip.Weight;
            }

            return BlendedFrame;
        }

        private Frame SampleFrame(PlaybackClip clip)
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
        /// <param name="looping">Whether the clip should loop when reaching the end.</param>
        /// <param name="warp">Whether re-entering the animation already playing should cross
        /// over into a second instance of it rather than restarting it in place.</param>
        private void TransitionToClip(Animation animation, float blendTime, bool looping, bool warp)
        {
            var transition = blendTime switch
            {
                > 0f => ClipTransition.Crossfade,
                -1f => ClipTransition.Manual,
                _ => ClipTransition.Instant,
            };

            var animName = animation.Name;

            if (warp && blendTime > 0f && activeClip?.Animation == animation)
            {
                animName = clips.TryGetValue(animName, out var primary) && primary == activeClip
                    ? animName + WarpSuffix
                    : animName;
            }

            // Check if clip already exists
            if (!clips.TryGetValue(animName, out var newClip))
            {
                newClip = new PlaybackClip(animation)
                {
                    Looping = looping,
                    Transition = transition,
                    BlendDuration = blendTime,
                    IsAdditive = animation.IsAdditive,
                    MaskName = animation is SequenceAnimation { BoneMaskName.Length: > 0 } newSequence ? newSequence.BoneMaskName : string.Empty,
                };
                clips[animName] = newClip;

                PrewarmAnimationSounds(animation);
            }
            else
            {
                newClip.Looping = looping;
                newClip.Transition = transition;
                newClip.BlendDuration = blendTime;

                newClip.IsPaused = false;
                newClip.Frame = 0;
            }

            if (animation is SequenceAnimation sequenceAnimation)
            {
                if (sequenceAnimation.AutoLayers.Length > 0)
                {
                    CreateAutoLayerClips(animName, newClip, sequenceAnimation);
                }

                if (sequenceAnimation.IsBlend)
                {
                    CreateBlendReferenceClips(animName, newClip, sequenceAnimation);
                }
            }

            if (activeClip == newClip)
            {
                previousClip = null;
                ZeroWeightsExcept(newClip);
                newClip.Weight = 1f;
            }
            else if (transition != ClipTransition.Instant && activeClip != null)
            {
                previousClip = activeClip;
                previousClip.Weight = 1f;

                ZeroWeightsExcept(previousClip, newClip);
                newClip.Weight = 0f;

                if (transition == ClipTransition.Crossfade)
                {
                    currentBlendTime = blendTime;
                }
            }
            else
            {
                previousClip = null;
                ZeroWeightsExcept(newClip);
                newClip.Weight = 1f;
            }

            if (transition == ClipTransition.Instant)
            {
                FrameCache.Clear();
            }

            activeClip = newClip;
        }

        /// <summary>Drops every clip but the given ones to zero weight.</summary>
        private void ZeroWeightsExcept(PlaybackClip keep, PlaybackClip? alsoKeep = null)
        {
            foreach (var clip in clips.Values)
            {
                if (clip != keep && clip != alsoKeep)
                {
                    clip.Weight = 0f;
                }
            }
        }

        /// <summary>
        /// Sets the blend weight for a clip with the specified animation name.
        /// </summary>
        /// <param name="name">The name of the animation.</param>
        /// <param name="weight">The weight value (0.0 to 1.0).</param>
        /// <param name="restartIfNew">Whether to restart the animation if it's just now fading in.</param>
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
        }

        /// <summary>
        /// Sets properties for a clip with the specified animation name.
        /// </summary>
        /// <param name="name">The name of the animation.</param>
        /// <param name="time">Optional playback time to set.</param>
        /// <param name="looping">Optional looping flag to set.</param>
        /// <param name="boneMask">Optional bone mask name to set.</param>
        public void SetAnimationProperties(string name, float? time = null, bool? looping = null, string? boneMask = null)
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
                    clip.MaskName = boneMask;
                }
            }
        }
    }
}
