using System.Linq;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelAnimation.SegmentDecoders;
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
        private AnimationSegmentDecoder?[] SegmentArray { get; } = [];

        private bool? hasFlexData;

        /// <inheritdoc/>
        public override bool HasFlexData => hasFlexData ??=
            Array.Exists(SegmentArray, segment => segment?.ChannelAttribute == AnimationChannelAttribute.Data);

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
        /// Gets the name of the bone mask (<c>m_nLocalWeightlist</c>) this sequence plays with. Empty
        /// for the default mask every animation gets unless it names one of its own.
        /// </summary>
        public string BoneMaskName { get; } = string.Empty;

        /// <summary>
        /// Gets whether this sequence blends between several animations along a pose parameter
        /// instead of playing a single one.
        /// </summary>
        public bool IsBlend => Fetch is { LocalReferenceArray.Length: > 1 } fetch && (fetch.Is1D || fetch.Is2D);

        /// <summary>
        /// Gets the name each entry of <see cref="AnimationFetch.LocalReferenceArray"/> resolves to
        /// against the sequence group's shared name array, in the same order <see cref="IsBlend"/>
        /// blends them in. Empty for a sequence that is not a blend, or was constructed without
        /// sequence data.
        /// </summary>
        public string[] BlendReferenceNames { get; } = [];

        /// <summary>
        /// Gets the name each dimension of <see cref="AnimationFetch.LocalPose"/> resolves to against
        /// the sequence group's pose parameter array, in the same order (row, then column for a 2D
        /// blend). Empty where the dimension is unused, is not a blend, or has no pose parameter
        /// (<see cref="AnimationFetch.FixedBlendWeight"/> instead), or the sequence was constructed
        /// without sequence data.
        /// </summary>
        public string[] PoseParameterNames { get; } = [];

        /// <summary>
        /// Gets the name the first entry of <see cref="AnimationFetch.LocalReferenceArray"/> resolves
        /// to against the sequence group's shared name array. That is the sequence's own animation for
        /// most sequences, and an animation another sequence already plays for the ones that exist
        /// only to give it a second name. Empty for a sequence that was constructed without sequence
        /// data.
        /// </summary>
        public string ReferencedAnimationName { get; } = string.Empty;

        /// <summary>
        /// Gets whether this animation was constructed from sequence data.
        /// </summary>
        public bool FromSequence { get; }

        private static AnimationLocalHierarchy[] GetLocalHierarchy(KVObject? animDesc)
            => animDesc?.GetArray("m_hierarchyArray")?.Select(static x => new AnimationLocalHierarchy(x)).ToArray() ?? [];

        private SequenceAnimation(KVObject animDesc, AnimationSegmentDecoder?[] segmentArray)
        {
            Name = animDesc.GetStringProperty("m_name");
            Fps = animDesc.GetFloatProperty("fps");
            SegmentArray = segmentArray;

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
        private SequenceAnimation(KVObject seqDesc, KVObject? animDesc, AnimationSegmentDecoder?[] segmentArray,
            string[] sequenceNameArray, string[] boneMaskNames, string[] poseParamNames)
        {
            // Name and metadata from sequence descriptor
            Name = seqDesc.GetStringProperty("m_sName");

            var seqFlags = seqDesc.GetSubCollection("m_flags");
            var animFlags = animDesc?.GetSubCollection("m_flags");

            IsLooping = seqFlags.GetBooleanProperty("m_bLooping");
            Hidden = seqFlags.GetBooleanProperty("m_bHidden");
            Delta = seqFlags.GetBooleanProperty("m_bLegacyDelta") || (animFlags?.GetBooleanProperty("m_bDelta") ?? false);

            Worldspace = seqFlags.GetBooleanProperty("m_bLegacyWorldspace");
            Realtime = seqFlags.GetBooleanProperty("m_bLegacyRealtime");
            Autoplay = seqFlags.GetBooleanProperty("m_bAutoplay");
            AnimGraphAdditive = animFlags?.GetBooleanProperty("m_bAnimGraphAdditive") ?? false;

            IsAdditive = Delta || AnimGraphAdditive;

            // Activities from sequence descriptor
            Activities = seqDesc.GetArray("m_activityArray")
                .Select(x => new AnimationActivity(x))
                .ToArray();

            LocalHierarchy = GetLocalHierarchy(animDesc);

            // Transition params from sequence descriptor
            var transition = seqDesc.GetSubCollection("m_transition");
            SequenceParams = new AnimationSequenceParams(transition);

            SegmentArray = segmentArray;
            Movements = [];
            Events = [];

            if (animDesc != null)
            {
                Fps = animDesc.GetFloatProperty("fps");

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
            }

            var autoLayerArray = seqDesc.GetArray("m_autoLayerArray");
            AutoLayers = new AnimationAutoLayer[autoLayerArray.Count];
            for (var i = 0; i < autoLayerArray.Count; i++)
            {
                var layer = new AnimationAutoLayer(autoLayerArray[i]);

                if (layer.LocalReference >= 0 && layer.LocalReference < sequenceNameArray.Length)
                {
                    layer.ReferencedAnimationName = sequenceNameArray[layer.LocalReference];
                }

                AutoLayers[i] = layer;
            }

            var fetch = seqDesc.GetSubCollection("m_fetch");
            Fetch = new AnimationFetch(fetch);

            if (Fetch.Value.LocalReferenceArray is [var firstReference, ..]
                && firstReference >= 0 && firstReference < sequenceNameArray.Length)
            {
                ReferencedAnimationName = sequenceNameArray[firstReference];
            }

            if (IsBlend)
            {
                var localReferenceArray = Fetch.Value.LocalReferenceArray;
                var blendReferenceNames = new string[localReferenceArray.Length];
                for (var i = 0; i < blendReferenceNames.Length; i++)
                {
                    var refIndex = (int)localReferenceArray[i];
                    blendReferenceNames[i] = refIndex >= 0 && refIndex < sequenceNameArray.Length
                        ? sequenceNameArray[refIndex]
                        : string.Empty;
                }
                BlendReferenceNames = blendReferenceNames;

                var localPose = Fetch.Value.LocalPose;
                var poseParameterNames = new string[localPose.Length];
                for (var i = 0; i < poseParameterNames.Length; i++)
                {
                    var poseIndex = (int)localPose[i];
                    poseParameterNames[i] = poseIndex >= 0 && poseIndex < poseParamNames.Length
                        ? poseParamNames[poseIndex]
                        : string.Empty;
                }
                PoseParameterNames = poseParameterNames;
            }

            var weightListIndex = seqDesc.GetInt32Property("m_nLocalWeightlist");
            BoneMaskName = weightListIndex > 0 && weightListIndex < boneMaskNames.Length
                ? boneMaskNames[weightListIndex]
                : string.Empty;

            FromSequence = true;
        }

        /// <summary>
        /// Builds animation segment decoders from animation data and decode key.
        /// </summary>
        private static AnimationSegmentDecoder?[] BuildSegmentArray(
            KVObject animationData,
            KVObject decodeKey,
            Skeleton skeleton,
            FlexController[] flexControllers)
        {
            var decoderArrayKV = animationData.GetArray("m_decoderArray");
            var decoderArray = new string[decoderArrayKV.Count];
            for (var i = 0; i < decoderArrayKV.Count; i++)
            {
                decoderArray[i] = decoderArrayKV[i].GetStringProperty("m_szName");
            }

            var userArrayKV = decodeKey.GetArray("m_userArray");
            var userNames = new string[userArrayKV?.Count ?? 0];
            for (var i = 0; i < userNames.Length; i++)
            {
                userNames[i] = userArrayKV![i].GetStringProperty("m_name");
            }

            var dataChannelArrayKV = decodeKey.GetArray("m_dataChannelArray");
            var dataChannelArray = new AnimationDataChannel[dataChannelArrayKV.Count];
            for (var i = 0; i < dataChannelArrayKV.Count; i++)
            {
                dataChannelArray[i] = new AnimationDataChannel(skeleton, flexControllers, userNames, dataChannelArrayKV[i]);
            }

            var segmentArrayKV = animationData.GetArray("m_segmentArray");
            var segmentArray = new AnimationSegmentDecoder?[segmentArrayKV.Count];
            for (var i = 0; i < segmentArrayKV.Count; i++)
            {
                var segmentKV = segmentArrayKV[i];
                var container = segmentKV.GetArray<byte>("m_container");
                var containerSpan = container.AsSpan();
                var localChannel = dataChannelArray[segmentKV.GetInt32Property("m_nLocalChannel")];

                // Read header
                var decoder = decoderArray[BitConverter.ToInt16(containerSpan[0..2])];
                //var cardinality = BitConverter.ToInt16(containerSpan[2..4]);
                var numElements = BitConverter.ToInt16(containerSpan[4..6]);
                //var totalLength = BitConverter.ToInt16(containerSpan[6..8]);

                // Read bone list
                var end = 8 + numElements * 2;
                var elements = MemoryMarshal.Cast<byte, short>(containerSpan[8..end]);
                var remapTable = new int[localChannel.RemapTable.Length];

                for (var j = 0; j < remapTable.Length; j++)
                {
                    remapTable[j] = elements.IndexOf((short)localChannel.RemapTable[j]);
                }

                var wantedElements = remapTable.Where(boneID => boneID != -1).ToArray();
                remapTable = remapTable
                    .Select((boneID, i) => (boneID, i))
                    .Where(t => t.boneID != -1)
                    .Select(t => t.i)
                    .ToArray();

                if (localChannel.Attribute == AnimationChannelAttribute.Unknown)
                {
                    Console.Error.WriteLine($"Unknown channel attribute encountered with '{decoder}' decoder");
                    continue;
                }

                var containerSegment = new ArraySegment<byte>(container, end, container.Length - end);

                // Look at the decoder to see what to read
                segmentArray[i] = decoder switch
                {
                    nameof(CCompressedStaticFullVector3) => new CCompressedStaticFullVector3(),
                    nameof(CCompressedStaticVector3) => new CCompressedStaticVector3(),
                    nameof(CCompressedStaticQuaternion) => new CCompressedStaticQuaternion(),
                    nameof(CCompressedStaticFloat) => new CCompressedStaticFloat(),

                    nameof(CCompressedFullVector3) => new CCompressedFullVector3(),
                    nameof(CCompressedDeltaVector3) => new CCompressedDeltaVector3(),
                    nameof(CCompressedAnimVector3) => new CCompressedAnimVector3(),
                    nameof(CCompressedAnimQuaternion) => new CCompressedAnimQuaternion(),
                    nameof(CCompressedFullQuaternion) => new CCompressedFullQuaternion(),
                    nameof(CCompressedFullFloat) => new CCompressedFullFloat(),
                    _ => null,
                };

                var segment = segmentArray[i];
                if (segment != null)
                {
                    segment.Initialize(containerSegment, wantedElements, remapTable, localChannel.Attribute, numElements);
                    continue;
                }

#if DEBUG
                Console.WriteLine($"Unhandled animation bone decoder type '{decoder}' for attribute '{localChannel.Attribute}'");
#endif
            }

            return segmentArray;
        }

        /// <summary>
        /// Creates animation instances from the provided animation data and decode key.
        /// </summary>
        public static IEnumerable<SequenceAnimation> FromData(KVObject animationData, KVObject decodeKey,
            Skeleton skeleton, FlexController[] flexControllers)
        {
            var animArray = animationData.GetArray("m_animArray");

            if (animArray.Count == 0)
            {
                return [];
            }

            var segmentArray = BuildSegmentArray(animationData, decodeKey, skeleton, flexControllers);

            return animArray
                .Select(anim => new SequenceAnimation(anim, segmentArray) { TargetSkeletonName = skeleton.Name })
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
            KVObject decodeKey,
            Skeleton skeleton,
            FlexController[] flexControllers)
        {
            var animArray = animationData.GetArray("m_animArray");

            if (animArray.Count == 0)
            {
                return [];
            }

            var segmentArray = BuildSegmentArray(animationData, decodeKey, skeleton, flexControllers);
            var sequenceNameArray = sequenceData.GetArray<string>("m_localSequenceNameArray");

            var boneMaskArray = sequenceData.GetArray("m_localBoneMaskArray");
            var boneMaskNames = new string[boneMaskArray?.Count ?? 0];
            for (var i = 0; i < boneMaskNames.Length; i++)
            {
                boneMaskNames[i] = boneMaskArray![i].GetStringProperty("m_sName");
            }

            var poseParamArray = sequenceData.GetArray("m_localPoseParamArray");
            var poseParamNames = new string[poseParamArray?.Count ?? 0];
            for (var i = 0; i < poseParamNames.Length; i++)
            {
                poseParamNames[i] = poseParamArray![i].GetStringProperty("m_sName");
            }

            // The sequence group's name table spells the same animation both ways in places, and the
            // compiler resolves it regardless of case.
            var animLookup = new Dictionary<string, KVObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var anim in animArray)
            {
                var name = anim.GetStringProperty("m_name");
                animLookup[name] = anim;
            }

            var processedAnimNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seqDescArray = sequenceData.GetArray("m_localS1SeqDescArray");
            var animations = new List<SequenceAnimation>();

            static KVObject? FirstReference(KVObject seqDesc, string[] sequenceNameArray, Dictionary<string, KVObject> animLookup)
            {
                var localRefArray = seqDesc.GetSubCollection("m_fetch").GetIntegerArray("m_localReferenceArray");

                if (localRefArray.Length == 0)
                {
                    return null;
                }

                var refIndex = (int)localRefArray[0];

                if (refIndex < 0 || refIndex >= sequenceNameArray.Length)
                {
                    return null;
                }

                return animLookup.GetValueOrDefault(sequenceNameArray[refIndex]);
            }

            // A reference names an entry of the shared name array, which is an animation for most
            // sequences but another sequence for the ones that blend generated animations.
            var sequenceLookup = new Dictionary<string, KVObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var seqDesc in seqDescArray)
            {
                if (FirstReference(seqDesc, sequenceNameArray, animLookup) is { } referenced)
                {
                    sequenceLookup[seqDesc.GetStringProperty("m_sName")] = referenced;
                }
            }

            foreach (var seqDesc in seqDescArray)
            {
                var fetch = seqDesc.GetSubCollection("m_fetch");

                var localRefArray = fetch.GetIntegerArray("m_localReferenceArray");
                if (localRefArray.Length == 0)
                {
                    continue;
                }

                // A blend plays all of its references at once; the first one stands in for the
                // sequence when a single animation is needed, the rest are kept on the fetch.
                var refIndex = (int)localRefArray[0];

                if (refIndex < 0 || refIndex >= sequenceNameArray.Length)
                {
                    continue;
                }

                var refAnimName = sequenceNameArray[refIndex];

                if (!animLookup.TryGetValue(refAnimName, out var animDesc)
                    && !sequenceLookup.TryGetValue(refAnimName, out animDesc)
                    && localRefArray.Length == 1)
                {
                    continue;
                }

                var seqName = seqDesc.GetStringProperty("m_sName");
                processedAnimNames.Add(seqName);

                animations.Add(new SequenceAnimation(seqDesc, animDesc, segmentArray, sequenceNameArray, boneMaskNames, poseParamNames) { TargetSkeletonName = skeleton.Name });
            }

            // Add remaining animations not already output as sequences
            foreach (var anim in animArray)
            {
                var animName = anim.GetStringProperty("m_name");

                if (processedAnimNames.Contains(animName))
                {
                    continue;
                }

                animations.Add(new SequenceAnimation(anim, segmentArray) { TargetSkeletonName = skeleton.Name });
            }

            return animations;
        }

        /// <summary>
        /// Creates animation instances from a resource file.
        /// </summary>
        public static IEnumerable<SequenceAnimation> FromResource(Resource resource, KVObject decodeKey, Skeleton skeleton, FlexController[] flexControllers)
            => FromData(GetAnimationData(resource), decodeKey, skeleton, flexControllers);

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

        private enum AnimatedChannels : byte
        {
            None = 0,
            Position = 1,
            Angle = 2,
        }

        /// <summary>
        /// Sequences decode into a bind-pose frame and only write the channels they animate (see
        /// <see cref="BuildAnimatedChannels"/>), so a channel the animation leaves alone holds the bind
        /// pose rather than a delta, and has to be neutralized before it is composed onto a pose.
        /// </summary>
        public override FrameBone GetAdditiveDelta(int boneIndex, FrameBone bone)
        {
            var animated = animatedChannelsCache ??= BuildAnimatedChannels();
            var channels = boneIndex < animated.Length ? animated[boneIndex] : AnimatedChannels.None;

            var position = (channels & AnimatedChannels.Position) != 0 ? bone.Position : Vector3.Zero;
            var angle = (channels & AnimatedChannels.Angle) != 0 ? bone.Angle : Quaternion.Identity;

            // Sequence scale is authored around one rather than around zero, so the delta is what it is
            // over one. That is also zero for a bone this animation does not scale, masking it for free.
            return new FrameBone(position, bone.Scale - 1f, angle);
        }

        /// <summary>
        /// Returns the bones this animation writes a scale for, empty for the animations that leave
        /// every bone at its rest scale. Sequence scale lives in its own channel, so this is what has
        /// to be decoded to recover a resized bone.
        /// </summary>
        public int[] GetScaledBones()
        {
            var scaled = new SortedSet<int>();

            foreach (var segment in SegmentArray)
            {
                if (segment is null || segment.ChannelAttribute != AnimationChannelAttribute.Scale)
                {
                    continue;
                }

                foreach (var boneIndex in segment.RemapTable)
                {
                    if (boneIndex >= 0)
                    {
                        scaled.Add(boneIndex);
                    }
                }
            }

            return [.. scaled];
        }

        private AnimatedChannels[]? animatedChannelsCache;

        /// <summary>
        /// Returns, per bone, which transform channels this animation actually writes, derived from
        /// the segment decoders' bone targets and channel attributes. Bones past the end of it are
        /// animated by nothing.
        /// </summary>
        private AnimatedChannels[] BuildAnimatedChannels()
        {
            var boneCount = 0;

            foreach (var segment in SegmentArray)
            {
                foreach (var boneIndex in segment?.RemapTable ?? [])
                {
                    boneCount = Math.Max(boneCount, boneIndex + 1);
                }
            }

            var animated = new AnimatedChannels[boneCount];

            foreach (var segment in SegmentArray)
            {
                if (segment is null)
                {
                    continue;
                }

                var channel = segment.ChannelAttribute switch
                {
                    AnimationChannelAttribute.Position => AnimatedChannels.Position,
                    AnimationChannelAttribute.Angle => AnimatedChannels.Angle,
                    _ => AnimatedChannels.None,
                };

                if (channel == AnimatedChannels.None)
                {
                    continue;
                }

                foreach (var boneIndex in segment.RemapTable)
                {
                    if (boneIndex >= 0)
                    {
                        animated[boneIndex] |= channel;
                    }
                }
            }

            return animated;
        }

        /// <inheritdoc/>
        public override void DecodeFrame(Frame outFrame)
        {
            foreach (var frameBlock in FrameBlocks)
            {
                // Only consider blocks that actually contain info for this frame
                if (outFrame.FrameIndex >= frameBlock.StartFrame && outFrame.FrameIndex <= frameBlock.EndFrame)
                {
                    foreach (var segmentIndex in frameBlock.SegmentIndexArray)
                    {
                        var segment = SegmentArray[segmentIndex];
                        // Segment could be null for unknown decoders
                        segment?.Read(outFrame.FrameIndex - frameBlock.StartFrame, outFrame);
                    }
                }
            }
        }
    }
}
