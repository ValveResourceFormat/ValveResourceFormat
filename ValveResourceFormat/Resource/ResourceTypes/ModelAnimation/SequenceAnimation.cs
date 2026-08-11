using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents a sequence-based model animation with frame data, events, and movement information.
    /// </summary>
    public sealed class SequenceAnimation : Animation
    {
        /// <summary>
        /// Gets a value indicating whether the animation loops.
        /// </summary>
        public bool IsLooping { get; }

        /// <summary>
        /// Gets a value indicating whether the animation is hidden.
        /// </summary>
        public bool Hidden { get; init; }

        /// <summary>
        /// Gets a value indicating whether this is a delta animation.
        /// </summary>
        public bool Delta { get; init; }

        /// <summary>
        /// Gets a value indicating whether the animation graph plays this animation additively
        /// (<c>m_bAnimGraphAdditive</c>, present in newer engine branches).
        /// </summary>
        public bool AnimGraphAdditive { get; }

        /// <summary>
        /// Gets a value indicating whether this animation is in world space.
        /// </summary>
        public bool Worldspace { get; init; }

        /// <summary>
        /// Gets LegacyRealtime value of animation sequence. False for animations constructed without animation sequence data.
        /// </summary>
        public bool Realtime { get; init; }

        /// <summary>
        /// Gets Autoplay value of animation sequence. False for animations constructed without animation sequence data.
        /// </summary>
        public bool Autoplay { get; init; }

        private AnimationFrameBlock[] FrameBlocks { get; } = [];
        private readonly AnimationSegmentSource segmentSource;

        private bool? hasFlexData;

        /// <inheritdoc/>
        public override bool HasFlexData => hasFlexData ??=
            Array.Exists(segmentSource.Segments, segment => segment.Decoder?.ChannelAttribute == AnimationChannelAttribute.Data);

        /// <inheritdoc/>
        /// <remarks>
        /// The skeleton this animation was last bound to, so it is empty until it has been bound or
        /// decoded once.
        /// </remarks>
        public override string TargetSkeletonName => skeletonBinding?.Skeleton.Name ?? string.Empty;

        /// <summary>
        /// Gets the movement data for this animation.
        /// </summary>
        public AnimationMovement[] Movements { get; }

        /// <summary>
        /// Gets the events defined in this animation.
        /// </summary>
        public AnimationEvent[] Events { get; }

        /// <summary>
        /// Gets the activities associated with this animation.
        /// </summary>
        public AnimationActivity[] Activities { get; }

        /// <summary>
        /// Gets the local-hierarchy overrides of this animation (bones interpolated in another bone's
        /// or model space for a frame range, e.g. a weapon detaching in a death animation).
        /// </summary>
        public AnimationLocalHierarchy[] LocalHierarchy { get; } = [];

        /// <summary>
        /// Gets the sequence parameters for this animation.
        /// </summary>
        public AnimationSequenceParams SequenceParams { get; }

        /// <summary>
        /// Gets the auto layer data for this animation. Empty for animations that were constructed without sequence data.
        /// </summary>
        public AnimationAutoLayer[] AutoLayers { get; } = [];

        /// <summary>
        /// Gets fetch data for this animation. Null for animations that were constructed without sequence data.
        /// </summary>
        public AnimationFetch? Fetch { get; }

        /// <summary>
        /// Gets whether this animation was constructed from sequence data.
        /// </summary>
        public bool FromSequence { get; }

        private static AnimationLocalHierarchy[] GetLocalHierarchy(KVObject animDesc)
            => animDesc.GetArray("m_hierarchyArray")?.Select(static x => new AnimationLocalHierarchy(x)).ToArray() ?? [];

        private SequenceAnimation(KVObject animDesc, AnimationSegmentSource segmentSource)
        {
            // Get animation properties
            Name = animDesc.GetStringProperty("m_name");
            Fps = animDesc.GetFloatProperty("fps");
            this.segmentSource = segmentSource;

            var flags = animDesc.GetSubCollection("m_flags");
            IsLooping = flags.GetBooleanProperty("m_bLooping");
            Hidden = flags.GetBooleanProperty("m_bHidden");
            Delta = flags.GetBooleanProperty("m_bDelta");
            AnimGraphAdditive = flags.GetBooleanProperty("m_bAnimGraphAdditive");
            Worldspace = flags.GetBooleanProperty("m_bLegacyWorldspace");
            IsAdditive = Delta || AnimGraphAdditive;

            var pData = animDesc.GetSubCollection("m_pData");
            FrameCount = pData.GetInt32Property("m_nFrames");

            var frameBlockArray = pData.GetArray("m_frameblockArray");
            FrameBlocks = new AnimationFrameBlock[frameBlockArray.Count];
            for (var i = 0; i < frameBlockArray.Count; i++)
            {
                FrameBlocks[i] = new AnimationFrameBlock(frameBlockArray[i]);
            }

            var movementArray = animDesc.GetArray("m_movementArray");
            Movements = new AnimationMovement[movementArray.Count];
            for (var i = 0; i < movementArray.Count; i++)
            {
                Movements[i] = new AnimationMovement(movementArray[i]);
            }

            Events = animDesc.GetArray("m_eventArray")
                                 .Select(x => new AnimationEvent(x))
                                 .ToArray();

            Activities = animDesc.GetArray("m_activityArray")
                                    .Select(x => new AnimationActivity(x))
                                    .ToArray();

            LocalHierarchy = GetLocalHierarchy(animDesc);

            var sequenceParams = animDesc.GetSubCollection("m_sequenceParams");
            SequenceParams = new AnimationSequenceParams(sequenceParams);

            FromSequence = false;
        }

        /// <summary>
        /// Constructor for creating animation from sequence descriptor (ASEQ) and animation data (ANIM).
        /// </summary>
        private SequenceAnimation(KVObject seqDesc, KVObject animDesc, AnimationSegmentSource segmentSource)
        {
            // Name and metadata from sequence descriptor
            Name = seqDesc.GetStringProperty("m_sName");

            var seqFlags = seqDesc.GetSubCollection("m_flags");
            var animFlags = animDesc.GetSubCollection("m_flags");

            IsLooping = seqFlags.GetBooleanProperty("m_bLooping");
            Hidden = seqFlags.GetBooleanProperty("m_bHidden");
            Delta = seqFlags.GetBooleanProperty("m_bLegacyDelta") || animFlags.GetBooleanProperty("m_bDelta");

            Worldspace = seqFlags.GetBooleanProperty("m_bLegacyWorldspace");
            Realtime = seqFlags.GetBooleanProperty("m_bLegacyRealtime");
            Autoplay = seqFlags.GetBooleanProperty("m_bAutoplay");
            AnimGraphAdditive = animFlags.GetBooleanProperty("m_bAnimGraphAdditive");

            IsAdditive = Delta || AnimGraphAdditive;

            // Activities from sequence descriptor
            Activities = seqDesc.GetArray("m_activityArray")
                .Select(x => new AnimationActivity(x))
                .ToArray();

            LocalHierarchy = GetLocalHierarchy(animDesc);

            // Transition params from sequence descriptor
            var transition = seqDesc.GetSubCollection("m_transition");
            SequenceParams = new AnimationSequenceParams(transition);

            // Animation data from ANIM block
            Fps = animDesc.GetFloatProperty("fps");
            this.segmentSource = segmentSource;

            var pData = animDesc.GetSubCollection("m_pData");
            FrameCount = pData.GetInt32Property("m_nFrames");

            var frameBlockArray = pData.GetArray("m_frameblockArray");
            FrameBlocks = new AnimationFrameBlock[frameBlockArray.Count];
            for (var i = 0; i < frameBlockArray.Count; i++)
            {
                FrameBlocks[i] = new AnimationFrameBlock(frameBlockArray[i]);
            }

            var movementArray = animDesc.GetArray("m_movementArray");
            Movements = new AnimationMovement[movementArray.Count];
            for (var i = 0; i < movementArray.Count; i++)
            {
                Movements[i] = new AnimationMovement(movementArray[i]);
            }

            // Events from animation data
            Events = animDesc.GetArray("m_eventArray")
                .Select(x => new AnimationEvent(x))
                .ToArray();

            // Auto layers
            var autoLayerArray = seqDesc.GetArray("m_autoLayerArray");
            AutoLayers = new AnimationAutoLayer[autoLayerArray.Count];
            for (var i = 0; i < autoLayerArray.Count; i++)
            {
                AutoLayers[i] = new AnimationAutoLayer(autoLayerArray[i]);
            }

            // Fetch
            var fetch = seqDesc.GetSubCollection("m_fetch");
            Fetch = new AnimationFetch(fetch);

            FromSequence = true;
        }

        /// <summary>
        /// Creates animation instances from the provided animation data and decode key.
        /// </summary>
        public static IEnumerable<SequenceAnimation> FromData(KVObject animationData, KVObject decodeKey)
        {
            var animArray = animationData.GetArray("m_animArray");

            if (animArray.Count == 0)
            {
                return [];
            }

            var segmentSource = new AnimationSegmentSource(animationData, decodeKey);

            return animArray
                .Select(anim => new SequenceAnimation(anim, segmentSource))
                .ToArray();
        }

        /// <summary>
        /// Creates animation instances from sequence data (ASEQ) and animation data (ANIM).
        /// This method uses sequence descriptors from ASEQ for names and metadata, while getting
        /// frame data from animations in ANIM that the sequences reference.
        /// </summary>
        public static IEnumerable<SequenceAnimation> FromSequenceData(
            KVObject sequenceData,
            KVObject animationData,
            KVObject decodeKey)
        {
            var animArray = animationData.GetArray("m_animArray");

            if (animArray.Count == 0)
            {
                return [];
            }

            var segmentSource = new AnimationSegmentSource(animationData, decodeKey);
            var sequenceNameArray = sequenceData.GetArray<string>("m_localSequenceNameArray");

            var animLookup = new Dictionary<string, KVObject>();
            foreach (var anim in animArray)
            {
                var name = anim.GetStringProperty("m_name");
                animLookup[name] = anim;
            }

            var processedAnimNames = new HashSet<string>();
            var seqDescArray = sequenceData.GetArray("m_localS1SeqDescArray");
            var animations = new List<SequenceAnimation>();

            foreach (var seqDesc in seqDescArray)
            {
                var fetch = seqDesc.GetSubCollection("m_fetch");

                var localRefArray = fetch.GetIntegerArray("m_localReferenceArray");
                if (localRefArray.Length == 0)
                {
                    continue;
                }

                // TODO: Handle multiple references for blend sequences - for now just use first
                var refIndex = (int)localRefArray[0];

                if (refIndex < 0 || refIndex >= sequenceNameArray.Length)
                {
                    continue;
                }

                var refAnimName = sequenceNameArray[refIndex];

                if (!animLookup.TryGetValue(refAnimName, out var animDesc))
                {
                    continue;
                }

                var seqName = seqDesc.GetStringProperty("m_sName");
                processedAnimNames.Add(seqName);

                animations.Add(new SequenceAnimation(seqDesc, animDesc, segmentSource));
            }

            // Add remaining animations not already output as sequences
            foreach (var anim in animArray)
            {
                var animName = anim.GetStringProperty("m_name");

                if (processedAnimNames.Contains(animName))
                {
                    continue;
                }

                animations.Add(new SequenceAnimation(anim, segmentSource));
            }

            return animations;
        }

        /// <summary>
        /// Creates animation instances from a resource file.
        /// </summary>
        public static IEnumerable<SequenceAnimation> FromResource(Resource resource, KVObject decodeKey)
            => FromData(GetAnimationData(resource), decodeKey);

        private static KVObject GetAnimationData(Resource resource)
            => (resource.DataBlock ?? throw new InvalidOperationException("Resource has no data block.")).AsKeyValueCollection();

        private int GetMovementIndexForTime(float time)
        {
            var frame = (int)MathF.Floor(time * Fps);
            return GetMovementIndexForFrame(frame);
        }

        private int GetMovementIndexForFrame(int frame)
        {
            for (var i = 0; i < Movements.Length; i++)
            {
                var movement = Movements[i];
                if (movement.EndFrame > frame)
                {
                    return i;
                }
            }
            return Movements.Length - 1;
        }

        /// <inheritdoc/>
        public override bool HasMovementData()
        {
            return Movements.Length > 0;
        }

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(float time)
        {
            if (!HasMovementData())
            {
                return new();
            }

            GetMovementForTime(time, out var movement, out var nextMovement, out var t);
            return AnimationMovement.Lerp(movement, nextMovement, t);
        }

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(int frame)
        {
            if (!HasMovementData())
            {
                return new();
            }

            var movementIndex = GetMovementIndexForFrame(frame);
            var lastMovement = movementIndex == 0 ? null : Movements[movementIndex - 1];
            var movement = Movements[movementIndex];

            var movementTime = frame / (float)movement.EndFrame;
            return AnimationMovement.Lerp(lastMovement, movement, movementTime);
        }

        /// <summary>
        /// Returns root motion data at the specified animation time for interpolation.
        /// </summary>
        private void GetMovementForTime(float time, out AnimationMovement? lastMovement, out AnimationMovement nextMovement, out float t)
        {
            time %= FrameCount / Fps;

            var nextMovementIndex = GetMovementIndexForTime(time);
            var lastMovementIndex = nextMovementIndex - 1;

            nextMovement = Movements[nextMovementIndex];
            if (nextMovementIndex == 0)
            {
                lastMovement = null;

                var movementTime = nextMovement.EndFrame / Fps;
                t = time / movementTime;
                return;
            }

            lastMovement = Movements[lastMovementIndex];

            var startTime = lastMovement.EndFrame / Fps;
            var endTime = nextMovement.EndFrame / Fps;

            var movementDuration = endTime - startTime;
            var elapsedTime = time - startTime;

            t = Math.Min(1f, elapsedTime / movementDuration);
        }

        private AnimationBinding? skeletonBinding;

        /// <summary>
        /// Builds this animation's decoders and its mapping onto the given skeleton, so that a following
        /// <see cref="DecodeFrame(Frame)"/> for that skeleton does no setup work.
        /// </summary>
        internal void EnsureBinding(Skeleton skeleton, FlexController[] flexControllers)
        {
            skeletonBinding = segmentSource.GetBinding(skeleton, flexControllers);
        }

        private enum AnimatedChannels : byte
        {
            None = 0,
            Position = 1,
            Angle = 2,
        }

        /// <summary>
        /// Sequences decode into a bind-pose frame and only write the channels they animate (see
        /// <see cref="GetAnimatedChannels"/>), so a channel the animation leaves alone holds the bind
        /// pose rather than a delta, and has to be neutralized before it is composed onto a pose.
        /// </summary>
        public override FrameBone GetAdditiveDelta(int boneIndex, FrameBone bone)
        {
            var animated = GetAnimatedChannels();
            var channels = boneIndex < animated.Length ? animated[boneIndex] : AnimatedChannels.None;

            var position = (channels & AnimatedChannels.Position) != 0 ? bone.Position : Vector3.Zero;
            var angle = (channels & AnimatedChannels.Angle) != 0 ? bone.Angle : Quaternion.Identity;

            // Sequence scale is authored around one rather than around zero, so the delta is what it is
            // over one. That is also zero for a bone this animation does not scale, masking it for free.
            return new FrameBone(position, bone.Scale - 1f, angle);
        }

        private AnimationBinding? animatedChannelsBinding;
        private AnimatedChannels[]? animatedChannelsCache;

        /// <summary>
        /// Returns, per bone, which transform channels this animation writes on the skeleton it is bound
        /// to: the binding already resolved which element of each segment drives which bone, so this is
        /// that grouped by channel attribute. Empty until the animation has been bound.
        /// </summary>
        private AnimatedChannels[] GetAnimatedChannels()
        {
            var binding = skeletonBinding;

            if (binding == null)
            {
                return [];
            }

            if (animatedChannelsBinding == binding)
            {
                return animatedChannelsCache!;
            }

            var segments = segmentSource.Segments;
            var animated = new AnimatedChannels[binding.Skeleton.Bones.Length];

            for (var i = 0; i < segments.Length; i++)
            {
                var channel = segments[i].Decoder?.ChannelAttribute switch
                {
                    AnimationChannelAttribute.Position => AnimatedChannels.Position,
                    AnimationChannelAttribute.Angle => AnimatedChannels.Angle,
                    _ => AnimatedChannels.None,
                };

                if (channel == AnimatedChannels.None)
                {
                    continue;
                }

                // Only bone channels get this far, so every remap lands on a bone.
                foreach (var remap in binding.GetRemaps(i))
                {
                    animated[remap.Dest] |= channel;
                }
            }

            animatedChannelsCache = animated;
            animatedChannelsBinding = binding;
            return animated;
        }

        /// <inheritdoc/>
        /// <remarks>The binding for the frame's skeleton is resolved here, and remembered for the next call.</remarks>
        public override void DecodeFrame(Frame outFrame)
        {
            var binding = skeletonBinding;

            if (binding == null || binding.Skeleton != outFrame.Skeleton)
            {
                binding = segmentSource.GetBinding(outFrame.Skeleton, outFrame.FlexControllers);
                skeletonBinding = binding;
            }

            DecodeFrame(outFrame, binding);
        }

        private void DecodeFrame(Frame outFrame, AnimationBinding binding)
        {
            var segmentArray = segmentSource.Segments;

            // Read all frame blocks
            foreach (var frameBlock in FrameBlocks)
            {
                // Only consider blocks that actually contain info for this frame
                if (outFrame.FrameIndex >= frameBlock.StartFrame && outFrame.FrameIndex <= frameBlock.EndFrame)
                {
                    foreach (var segmentIndex in frameBlock.SegmentIndexArray)
                    {
                        // Decoder could be null for unknown decoders
                        var decoder = segmentArray[segmentIndex].Decoder;
                        decoder?.Read(
                            outFrame.FrameIndex - frameBlock.StartFrame,
                            outFrame,
                            binding.GetRemaps((int)segmentIndex));
                    }
                }
            }
        }
    }
}
