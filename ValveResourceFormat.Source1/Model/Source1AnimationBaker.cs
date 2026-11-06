using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.ModelData;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// Bakes the sequences of a Source 1 model into the embedded animation data of a Source 2 model.
/// Each animation of a sequence is decoded to full precision frames of bone-local transforms, and the sequence
/// keeps its blend grid, pose parameters, autolayers, events, activity and bone weights as a legacy sequence.
/// </summary>
internal sealed class Source1AnimationBaker
{
    private const int PositionDecoder = 0;
    private const int QuaternionDecoder = 1;
    private const int PositionChannel = 0;
    private const int AngleChannel = 1;
    internal const string PlaySoundEventName = "AE_CL_PLAYSOUND";
    internal const int ScriptSoundEventId = 1004;
    internal const int ScriptVoiceSoundEventId = 1008;
    internal const int ClientSoundEventId = 5004;

    private readonly Source1Model source;
    private readonly List<string> names = [];
    private readonly Dictionary<string, int> nameIndices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> sequenceNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Source1Animation, int> firstSegments = [];
    private readonly List<float[]> boneMaskWeights = [];
    private readonly KVObject animations = KVObject.Array();
    private readonly KVObject segments = KVObject.Array();
    private readonly KVObject sequences = KVObject.Array();
    private readonly KVObject boneMasks = KVObject.Array();

    private readonly Source1BoneControllerSet? controllers;

    // The controllers hold their default values in the animation, and the pose parameters move them from there
    private readonly float[] restValues;

    private Source1AnimationBaker(Source1Model source)
    {
        this.source = source;
        controllers = Source1BoneControllerSet.Create(source);
        restValues = controllers?.GetRestValues() ?? [];
    }

    /// <summary>Bakes the sequences of a model that have all their data.</summary>
    /// <param name="source">The model to bake the sequences of.</param>
    /// <returns>The animation data, or <see langword="null"/> when the model has no sequence that can be played.</returns>
    public static ModelAnimationSource? Bake(Source1Model source)
    {
        return new Source1AnimationBaker(source).Bake();
    }

    private ModelAnimationSource? Bake()
    {
        var playable = new List<Source1Sequence>();

        foreach (var sequence in source.Sequences)
        {
            if (sequence.IsPlayable && TryAddSegments(sequence))
            {
                playable.Add(sequence);
                sequenceNames.Add(sequence.Name);
                AddName(sequence.Name);
            }
        }

        if (playable.Count == 0)
        {
            return null;
        }

        // The mask at index zero stands for a sequence that has no weight list
        boneMasks.Add(CreateBoneMask(string.Empty, []));

        foreach (var sequence in playable)
        {
            AddSequence(sequence);
        }

        return new ModelAnimationSource(CreateAnimationGroup(), CreateAnimationData(), CreateSequenceGroup());
    }

    private bool TryAddSegments(Source1Sequence sequence)
    {
        try
        {
            foreach (var blend in sequence.Blends)
            {
                GetFirstSegment(blend);
            }

            return true;
        }
        catch (InvalidDataException e)
        {
            source.Warnings.Add($"Sequence '{sequence.Name}' was skipped, its animation data is invalid: {e.Message}");
            return false;
        }
    }

    private int AddName(string name)
    {
        if (nameIndices.TryGetValue(name, out var index))
        {
            return index;
        }

        index = names.Count;
        names.Add(name);
        nameIndices[name] = index;

        return index;
    }

    private void AddSequence(Source1Sequence sequence)
    {
        var blendCount = sequence.Blends.Length;
        var fetch = CreateFetch(sequence);
        var references = KVObject.Array();
        var referencedCount = fetch.Count;

        for (var i = 0; i < referencedCount; i++)
        {
            var animationName = blendCount == 1
                ? sequence.Name
                : string.Create(CultureInfo.InvariantCulture, $"{sequence.Name}$blend{i}");

            references.Add(AddName(animationName));
            AddAnimation(animationName, sequence, sequence.Blends[i], i);
        }

        fetch.Data.Add("m_localReferenceArray", references);

        var layers = CreateAutoLayers(sequence);
        AddControllerLayers(sequence, layers);

        sequences.Add(CreateSequenceDescription(
            sequence.Name,
            CreateSequenceFlags(sequence),
            CreateActivities(sequence),
            CreateTransition(sequence),
            layers,
            fetch.Data,
            GetBoneMaskIndex(sequence)));
    }

    private void AddAnimation(string name, Source1Sequence sequence, Source1Animation animation, int blendIndex)
    {
        var firstSegment = GetFirstSegment(animation);
        var lastFrame = animation.FrameCount - 1;

        var movements = KVObject.Array();

        foreach (var movement in animation.Movements)
        {
            movements.Add(CreateMovement(movement));
        }

        var events = KVObject.Array();

        if (blendIndex == 0)
        {
            foreach (var sequenceEvent in sequence.Events)
            {
                events.Add(CreateEvent(sequenceEvent, lastFrame));
            }
        }

        var isHidden = sequence.Blends.Length > 1;
        var animDesc = CreateAnimation(name, animation.Fps, animation.FrameCount, firstSegment, sequence.IsLooping, isHidden, animation.IsDelta, movements, events);

        animations.Add(animDesc);
    }

    /// <summary>Creates an animation of full precision frames, whose segments start at <paramref name="firstSegment"/>.</summary>
    internal static KVObject CreateAnimation(
        string name,
        float fps,
        int frameCount,
        int firstSegment,
        bool isLooping,
        bool isHidden,
        bool isDelta,
        KVObject movements,
        KVObject events)
    {
        var segmentIndices = KVObject.Array();
        segmentIndices.Add(firstSegment + PositionChannel);
        segmentIndices.Add(firstSegment + AngleChannel);

        var frameBlock = KVObject.Collection();
        frameBlock.Add("m_nStartFrame", 0);
        frameBlock.Add("m_nEndFrame", frameCount - 1);
        frameBlock.Add("m_segmentIndexArray", segmentIndices);

        var frameBlocks = KVObject.Array();
        frameBlocks.Add(frameBlock);

        var data = KVObject.Collection();
        data.Add("m_nFrames", frameCount);
        data.Add("m_frameblockArray", frameBlocks);

        var flags = KVObject.Collection();
        flags.Add("m_bLooping", isLooping);
        flags.Add("m_bHidden", isHidden);
        flags.Add("m_bDelta", isDelta);

        var animDesc = KVObject.Collection();
        animDesc.Add("m_name", name);
        animDesc.Add("fps", fps);
        animDesc.Add("m_flags", flags);
        animDesc.Add("m_pData", data);
        animDesc.Add("m_movementArray", movements);
        animDesc.Add("m_eventArray", events);
        animDesc.Add("m_activityArray", KVObject.Array());

        return animDesc;
    }

    private int GetFirstSegment(Source1Animation animation)
    {
        if (firstSegments.TryGetValue(animation, out var first))
        {
            return first;
        }

        var boneCount = source.Bones.Length;
        var frameCount = animation.FrameCount;
        DecodeFrames(animation, out var positions, out var rotations);

        for (var frame = 0; frame < frameCount; frame++)
        {
            var framePositions = positions.AsSpan(frame * boneCount, boneCount);
            var frameRotations = rotations.AsSpan(frame * boneCount, boneCount);

            if (controllers != null && !animation.IsDelta)
            {
                controllers.Apply(restValues, framePositions, frameRotations);
            }

            Source1ModelConverter.ToBoneFrames(source, framePositions, frameRotations, animation.IsDelta);
        }

        first = AddSegments(segments, boneCount, positions, rotations);
        firstSegments[animation] = first;

        return first;
    }

    // Decodes the bone-local transforms of every frame, in the space of the Source 1 bones
    private void DecodeFrames(Source1Animation animation, out Vector3[] positions, out Quaternion[] rotations)
    {
        var boneCount = source.Bones.Length;
        var frameCount = animation.FrameCount;
        positions = new Vector3[frameCount * boneCount];
        rotations = new Quaternion[frameCount * boneCount];

        var ik = animation.CreateIkApplier();

        for (var frame = 0; frame < frameCount; frame++)
        {
            var framePositions = positions.AsSpan(frame * boneCount, boneCount);
            var frameRotations = rotations.AsSpan(frame * boneCount, boneCount);

            animation.GetFrame(frame, framePositions, frameRotations);
            ik?.Apply(frame, framePositions, frameRotations);
        }
    }

    // Each way a bone controller can move from its default is a chain of steps, each a hidden additive sequence that
    // a layer blends in as the pose parameter of the controller passes through the step
    private void AddControllerLayers(Source1Sequence sequence, KVObject layers)
    {
        if (controllers == null || sequence.IsDelta)
        {
            return;
        }

        var animation = sequence.Blends[0];
        DecodeFrames(animation, out var restPositions, out var restRotations);

        foreach (var group in controllers.Groups)
        {
            if (group.High > group.Rest)
            {
                AddControllerChain(controllers, layers, sequence, animation, restPositions, restRotations, group, group.High);
            }

            if (group.Low < group.Rest)
            {
                AddControllerChain(controllers, layers, sequence, animation, restPositions, restRotations, group, group.Low);
            }
        }
    }

    private void AddControllerChain(
        Source1BoneControllerSet controllerSet,
        KVObject layers,
        Source1Sequence sequence,
        Source1Animation animation,
        Vector3[] restPositions,
        Quaternion[] restRotations,
        Source1BoneControllerSet.Group group,
        float end)
    {
        var isUpward = end > group.Rest;
        var direction = isUpward ? "+" : "-";
        var steps = group.GetSteps(end);
        var parameter = source.PoseParameters.Length + group.Index;

        for (var step = 1; step < steps.Length; step++)
        {
            var from = steps[step - 1];
            var to = steps[step];
            var name = string.Create(CultureInfo.InvariantCulture, $"{sequence.Name}${group.Name}{direction}{step}");
            var reference = AddControllerDelta(controllerSet, sequence, animation, name, restPositions, restRotations, group, from, to);

            // The ranges leave a margin so that the weight stays at one up to the end of the parameter
            if (isUpward)
            {
                layers.Add(CreateControllerLayer(reference, parameter, from, to, group.High + 1f, group.High + 1f));
            }
            else
            {
                layers.Add(CreateControllerLayer(reference, parameter, group.Low - 1f, group.Low, to, from));
            }
        }
    }

    // Bakes how far the bones of a group move when its input goes from one value to another, as a hidden additive sequence
    private int AddControllerDelta(
        Source1BoneControllerSet controllerSet,
        Source1Sequence sequence,
        Source1Animation animation,
        string name,
        Vector3[] restPositions,
        Quaternion[] restRotations,
        Source1BoneControllerSet.Group group,
        float from,
        float to)
    {
        var boneCount = source.Bones.Length;
        var frameCount = animation.FrameCount;
        var bones = group.Bones;
        var positions = new Vector3[frameCount * bones.Length];
        var rotations = new Quaternion[frameCount * bones.Length];
        var framePositions = new Vector3[boneCount];
        var frameRotations = new Quaternion[boneCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            Array.Fill(framePositions, Vector3.Zero);
            Array.Fill(frameRotations, Quaternion.Identity);

            foreach (var bone in bones)
            {
                var restIndex = (frame * boneCount) + bone;

                restValues[group.Index] = from;
                var fromPosition = restPositions[restIndex];
                var fromRotation = restRotations[restIndex];
                controllerSet.Apply(bone, restValues, ref fromPosition, ref fromRotation);

                restValues[group.Index] = to;
                var toPosition = restPositions[restIndex];
                var toRotation = restRotations[restIndex];
                controllerSet.Apply(bone, restValues, ref toPosition, ref toRotation);

                var delta = Quaternion.Normalize(Quaternion.Inverse(fromRotation) * toRotation);

                // Both signs are the same rotation, and blending goes the short way round
                framePositions[bone] = toPosition - fromPosition;
                frameRotations[bone] = delta.W < 0f ? Quaternion.Negate(delta) : delta;
            }

            Source1ModelConverter.ToBoneFrames(source, framePositions, frameRotations, true);

            for (var i = 0; i < bones.Length; i++)
            {
                positions[(frame * bones.Length) + i] = framePositions[bones[i]];
                rotations[(frame * bones.Length) + i] = frameRotations[bones[i]];
            }
        }

        restValues[group.Index] = group.Rest;

        var nameIndex = AddName(name);
        var firstSegment = AddSegments(segments, bones, positions, rotations);
        animations.Add(CreateAnimation(name, animation.Fps, frameCount, firstSegment, sequence.IsLooping, true, true, KVObject.Array(), KVObject.Array()));
        sequences.Add(CreateControllerDeltaSequence(name, sequence.IsLooping, nameIndex));

        return nameIndex;
    }

    /// <summary>Creates a hidden additive sequence that plays one animation.</summary>
    internal static KVObject CreateControllerDeltaSequence(string name, bool isLooping, int animationName)
    {
        var references = KVObject.Array();
        references.Add(animationName);

        var fetch = CreateFetch(KVObject.Collection(), KVObject.Array(), KVObject.Array(), KVObject.Array(), KVObject.Array(), -1);
        fetch.Add("m_localReferenceArray", references);

        var flags = CreateSequenceFlags(isLooping, true, true, false, false, false);

        return CreateSequenceDescription(name, flags, KVObject.Array(), KVObject.Collection(), KVObject.Array(), fetch, 0);
    }

    /// <summary>Creates a sequence description, whose fetch lists the animations it plays.</summary>
    internal static KVObject CreateSequenceDescription(string name, KVObject flags, KVObject activities, KVObject transition, KVObject layers, KVObject fetch, int boneMask)
    {
        var seqDesc = KVObject.Collection();
        seqDesc.Add("m_sName", name);
        seqDesc.Add("m_flags", flags);
        seqDesc.Add("m_activityArray", activities);
        seqDesc.Add("m_transition", transition);
        seqDesc.Add("m_autoLayerArray", layers);
        seqDesc.Add("m_fetch", fetch);
        seqDesc.Add("m_nLocalWeightlist", boneMask);

        return seqDesc;
    }

    /// <summary>Creates the flags of a sequence description.</summary>
    internal static KVObject CreateSequenceFlags(bool isLooping, bool isHidden, bool isDelta, bool isWorldspace, bool isRealtime, bool isAutoplay)
    {
        var flags = KVObject.Collection();
        flags.Add("m_bLooping", isLooping);
        flags.Add("m_bHidden", isHidden);
        flags.Add("m_bLegacyDelta", isDelta);
        flags.Add("m_bLegacyWorldspace", isWorldspace);
        flags.Add("m_bLegacyRealtime", isRealtime);
        flags.Add("m_bAutoplay", isAutoplay);

        return flags;
    }

    /// <summary>Creates the fetch of a sequence description. The caller adds the list of animations it plays.</summary>
    internal static KVObject CreateFetch(KVObject flags, KVObject localPose, KVObject keys0, KVObject keys1, KVObject groupSize, int cyclePoseParameter)
    {
        var fetch = KVObject.Collection();
        fetch.Add("m_flags", flags);
        fetch.Add("m_nLocalPose", localPose);
        fetch.Add("m_poseKeyArray0", keys0);
        fetch.Add("m_poseKeyArray1", keys1);
        fetch.Add("m_nGroupSize", groupSize);
        fetch.Add("m_nLocalCyclePoseParameter", cyclePoseParameter);

        return fetch;
    }

    /// <summary>Creates a layer that weights a sequence by a pose parameter, which rises from zero at <paramref name="start"/> to one at <paramref name="peak"/> and falls to zero from <paramref name="tail"/> to <paramref name="end"/>.</summary>
    internal static KVObject CreateControllerLayer(int reference, int parameter, float start, float peak, float tail, float end)
    {
        var flags = KVObject.Collection();
        flags.Add("m_bPost", false);
        flags.Add("m_bSpline", false);
        flags.Add("m_bXFade", false);
        flags.Add("m_bNoBlend", false);
        flags.Add("m_bLocal", false);
        flags.Add("m_bPose", true);

        var layer = KVObject.Collection();
        layer.Add("m_nLocalReference", reference);
        layer.Add("m_nLocalPose", parameter);
        layer.Add("m_flags", flags);
        layer.Add("m_start", start);
        layer.Add("m_peak", peak);
        layer.Add("m_tail", tail);
        layer.Add("m_end", end);

        return layer;
    }

    /// <summary>
    /// Adds the position and angle segments of an animation, which hold the local transform of every bone in every frame in turn.
    /// </summary>
    /// <returns>The index of the first of the two segments.</returns>
    internal static int AddSegments(KVObject segments, int boneCount, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Quaternion> rotations)
    {
        var bones = new int[boneCount];

        for (var i = 0; i < bones.Length; i++)
        {
            bones[i] = i;
        }

        return AddSegments(segments, bones, positions, rotations);
    }

    /// <summary>
    /// Adds the position and angle segments of an animation that moves only the given bones, in frames that hold those bones in turn.
    /// </summary>
    /// <returns>The index of the first of the two segments.</returns>
    internal static int AddSegments(KVObject segments, ReadOnlySpan<int> bones, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Quaternion> rotations)
    {
        var first = segments.Count;
        segments.Add(CreateSegment(bones, PositionChannel, PositionDecoder, MemoryMarshal.AsBytes(positions)));
        segments.Add(CreateSegment(bones, AngleChannel, QuaternionDecoder, MemoryMarshal.AsBytes(rotations)));

        return first;
    }

    // The container is the header of the segment, the channel elements it stores, then the data of every frame in turn
    private static KVObject CreateSegment(ReadOnlySpan<int> bones, int channel, int decoder, ReadOnlySpan<byte> frames)
    {
        const int headerSize = 8;
        var boneCount = bones.Length;
        var elementsSize = boneCount * sizeof(short);
        var container = new byte[headerSize + elementsSize + frames.Length];

        BinaryPrimitives.WriteInt16LittleEndian(container.AsSpan(0), (short)decoder);
        BinaryPrimitives.WriteInt16LittleEndian(container.AsSpan(2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(container.AsSpan(4), (short)boneCount);

        for (var i = 0; i < boneCount; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(container.AsSpan(headerSize + (i * sizeof(short))), (short)bones[i]);
        }

        frames.CopyTo(container.AsSpan(headerSize + elementsSize));

        var segment = KVObject.Collection();
        segment.Add("m_nLocalChannel", channel);
        segment.Add("m_container", KVObject.Blob(container));

        return segment;
    }

    private (KVObject Data, int Count) CreateFetch(Source1Sequence sequence)
    {
        var countX = sequence.BlendCountX;
        var countY = sequence.BlendCountY;
        var poseCount = source.PoseParameters.Length;
        var isGrid = countX > 1 && countY > 1;
        var isLine = !isGrid && sequence.Blends.Length > 1;
        var axis = countX > 1 ? 0 : 1;
        var axisPoseIndex = axis == 0 ? sequence.BlendIndexX : sequence.BlendIndexY;
        var isDriven = isGrid
            ? (uint)sequence.BlendIndexX < (uint)poseCount && (uint)sequence.BlendIndexY < (uint)poseCount
            : isLine && (uint)axisPoseIndex < (uint)poseCount;

        var flags = KVObject.Collection();
        var localPose = KVObject.Array();
        var keys0 = KVObject.Array();
        var keys1 = KVObject.Array();
        var groupSize = KVObject.Array();
        var referencedCount = 1;

        if (isDriven && isGrid)
        {
            flags.Add("m_b2D", true);
            flags.Add("m_b2D_TRI", true);
            localPose.Add(sequence.BlendIndexX);
            localPose.Add(sequence.BlendIndexY);
            groupSize.Add(countX);
            groupSize.Add(countY);

            foreach (var key in GetBlendKeys(sequence, 0))
            {
                keys0.Add(key);
            }

            var rowKeys = GetBlendKeys(sequence, 1);

            for (var i = 0; i < sequence.Blends.Length; i++)
            {
                keys1.Add(rowKeys[i / countX]);
            }

            referencedCount = sequence.Blends.Length;
        }
        else if (isDriven)
        {
            flags.Add("m_b1D", true);
            localPose.Add(axisPoseIndex);
            groupSize.Add(sequence.Blends.Length);

            foreach (var key in GetBlendKeys(sequence, axis))
            {
                keys0.Add(key);
            }

            referencedCount = sequence.Blends.Length;
        }

        var cyclePoseParameter = (sequence.Flags & Source1AnimationFlags.CyclePose) != 0 ? sequence.CyclePoseIndex : -1;
        var fetch = CreateFetch(flags, localPose, keys0, keys1, groupSize, cyclePoseParameter);

        return (fetch, referencedCount);
    }

    // The pose parameter value each animation of one blend axis sits at
    private static float[] GetBlendKeys(Source1Sequence sequence, int axis)
    {
        var count = axis == 0 ? sequence.BlendCountX : sequence.BlendCountY;
        var keys = new float[count];

        if (sequence.PoseKeys.Length > 0)
        {
            var offset = axis == 0 ? 0 : sequence.BlendCountX;
            Array.Copy(sequence.PoseKeys, offset, keys, 0, count);

            return keys;
        }

        var start = axis == 0 ? sequence.BlendStartX : sequence.BlendStartY;
        var end = axis == 0 ? sequence.BlendEndX : sequence.BlendEndY;

        for (var i = 0; i < count; i++)
        {
            var fraction = count > 1 ? (float)i / (count - 1) : 0f;
            keys[i] = start + ((end - start) * fraction);
        }

        return keys;
    }

    private static KVObject CreateSequenceFlags(Source1Sequence sequence)
    {
        return CreateSequenceFlags(
            sequence.IsLooping,
            (sequence.Flags & Source1AnimationFlags.Hidden) != 0,
            sequence.IsDelta,
            (sequence.Flags & Source1AnimationFlags.World) != 0,
            (sequence.Flags & Source1AnimationFlags.Realtime) != 0,
            (sequence.Flags & Source1AnimationFlags.AutoPlay) != 0);
    }

    private static KVObject CreateActivities(Source1Sequence sequence)
    {
        var activities = KVObject.Array();

        if (sequence.ActivityName.Length == 0)
        {
            return activities;
        }

        var activity = KVObject.Collection();
        activity.Add("m_name", sequence.ActivityName);
        activity.Add("m_nActivity", sequence.Activity);
        activity.Add("m_nFlags", 0);
        activity.Add("m_nWeight", sequence.ActivityWeight);
        activities.Add(activity);

        return activities;
    }

    private static KVObject CreateTransition(Source1Sequence sequence)
    {
        var transition = KVObject.Collection();
        transition.Add("m_flFadeInTime", sequence.FadeInTime);
        transition.Add("m_flFadeOutTime", sequence.FadeOutTime);

        return transition;
    }

    private KVObject CreateAutoLayers(Source1Sequence sequence)
    {
        var layers = KVObject.Array();
        var hasLocalContext = (sequence.Flags & Source1AnimationFlags.Local) != 0;

        foreach (var sourceLayer in sequence.AutoLayers)
        {
            if (sourceLayer.Sequence == null || !sequenceNames.Contains(sourceLayer.Sequence.Name))
            {
                continue;
            }

            var isLocal = (sourceLayer.Flags & Source1AutoLayerFlags.Local) != 0;

            if (isLocal && !hasLocalContext)
            {
                continue;
            }

            var flags = KVObject.Collection();
            flags.Add("m_bPost", (sourceLayer.Flags & Source1AutoLayerFlags.Post) != 0);
            flags.Add("m_bSpline", (sourceLayer.Flags & Source1AutoLayerFlags.Spline) != 0);
            flags.Add("m_bXFade", (sourceLayer.Flags & Source1AutoLayerFlags.XFade) != 0);
            flags.Add("m_bNoBlend", (sourceLayer.Flags & Source1AutoLayerFlags.NoBlend) != 0);
            flags.Add("m_bLocal", isLocal);
            flags.Add("m_bPose", (sourceLayer.Flags & Source1AutoLayerFlags.Pose) != 0);

            var layer = KVObject.Collection();
            layer.Add("m_nLocalReference", AddName(sourceLayer.Sequence.Name));
            layer.Add("m_nLocalPose", sourceLayer.PoseParameterIndex);
            layer.Add("m_flags", flags);
            layer.Add("m_start", sourceLayer.Start);
            layer.Add("m_peak", sourceLayer.Peak);
            layer.Add("m_tail", sourceLayer.Tail);
            layer.Add("m_end", sourceLayer.End);
            layers.Add(layer);
        }

        return layers;
    }

    private int GetBoneMaskIndex(Source1Sequence sequence)
    {
        var weights = sequence.BoneWeights;

        if (Array.TrueForAll(weights, static weight => weight == 1f) || !Array.Exists(weights, static weight => weight > 0f))
        {
            return 0;
        }

        // The mask at index zero is not in the list
        for (var i = 0; i < boneMaskWeights.Count; i++)
        {
            if (boneMaskWeights[i].AsSpan().SequenceEqual(weights))
            {
                return i + 1;
            }
        }

        boneMaskWeights.Add(weights);
        boneMasks.Add(CreateBoneMask(sequence.Name, weights));

        return boneMasks.Count - 1;
    }

    internal static KVObject CreateBoneMask(string name, float[] weights)
    {
        var bones = KVObject.Array();
        var boneWeights = KVObject.Array();

        for (var i = 0; i < weights.Length; i++)
        {
            if (weights[i] > 0f)
            {
                bones.Add(i);
                boneWeights.Add(weights[i]);
            }
        }

        var mask = KVObject.Collection();
        mask.Add("m_sName", name);
        mask.Add("m_nLocalBoneArray", bones);
        mask.Add("m_flBoneWeightArray", boneWeights);

        return mask;
    }

    private static KVObject CreateMovement(Source1Movement movement)
    {
        var data = KVObject.Collection();
        data.Add("endframe", movement.EndFrame);
        data.Add("motionflags", movement.Flags);
        data.Add("v0", movement.StartVelocity);
        data.Add("v1", movement.EndVelocity);
        data.Add("angle", movement.Angle);
        data.Add("vector", KVHelpers.ToKVArray(movement.Vector));
        data.Add("position", KVHelpers.ToKVArray(movement.Position));

        return data;
    }

    private static KVObject CreateEvent(Source1AnimationEvent sequenceEvent, int lastFrame)
    {
        var isSound = IsSoundEvent(sequenceEvent);
        string name;

        if (isSound)
        {
            name = PlaySoundEventName;
        }
        else if (sequenceEvent.Name.Length > 0)
        {
            name = sequenceEvent.Name;
        }
        else
        {
            name = sequenceEvent.EventId.ToString(CultureInfo.InvariantCulture);
        }

        var data = KVObject.Collection();
        data.Add("m_sEventName", name);
        data.Add("m_nFrame", (int)MathF.Round(sequenceEvent.Cycle * lastFrame));
        data.Add("m_flCycle", sequenceEvent.Cycle);
        data.Add("m_sOptions", sequenceEvent.Options);

        if (isSound)
        {
            data.Add("m_EventData", CreateSoundEventData(sequenceEvent.Options));
        }

        return data;
    }

    internal static KVObject CreateSoundEventData(string soundName)
    {
        var eventData = KVObject.Collection();
        var eventName = Source1Paths.IsWaveName(soundName) ? Source1Paths.NormalizeWaveName(soundName) : soundName;
        eventData.Add("name", eventName);

        return eventData;
    }

    // The client sound event and the scripted sounds, by name or by event id
    private static bool IsSoundEvent(Source1AnimationEvent sequenceEvent)
    {
        if (sequenceEvent.Options.Length == 0)
        {
            return false;
        }

        if (sequenceEvent.Name.Length > 0)
        {
            return sequenceEvent.Name is PlaySoundEventName or "SCRIPT_EVENT_SOUND" or "SCRIPT_EVENT_SOUND_VOICE";
        }

        return sequenceEvent.EventId is ClientSoundEventId or ScriptSoundEventId or ScriptVoiceSoundEventId;
    }

    private KVObject CreateAnimationGroup() => CreateAnimationGroup(GetBoneNames());

    private List<string> GetBoneNames()
    {
        var boneNames = new List<string>(source.Bones.Length);

        foreach (var bone in source.Bones)
        {
            boneNames.Add(bone.Name);
        }

        return boneNames;
    }

    internal static KVObject CreateBoneNameArray(IReadOnlyList<string> boneNames)
    {
        var array = KVObject.Array();

        foreach (var boneName in boneNames)
        {
            array.Add(boneName);
        }

        return array;
    }

    internal static KVObject CreateAnimationGroup(IReadOnlyList<string> boneNames)
    {
        var channels = KVObject.Array();
        channels.Add(CreateChannel("Position", boneNames));
        channels.Add(CreateChannel("Angle", boneNames));

        var decodeKey = KVObject.Collection();
        decodeKey.Add("m_dataChannelArray", channels);
        decodeKey.Add("m_userArray", KVObject.Array());

        var group = KVObject.Collection();
        group.Add("m_decodeKey", decodeKey);

        return group;
    }

    private static KVObject CreateChannel(string variableName, IReadOnlyList<string> boneNames)
    {
        var elementNames = KVObject.Array();
        var elementIndices = KVObject.Array();

        for (var i = 0; i < boneNames.Count; i++)
        {
            elementNames.Add(boneNames[i]);
            elementIndices.Add(i);
        }

        var channel = KVObject.Collection();
        channel.Add("m_szElementNameArray", elementNames);
        channel.Add("m_nElementIndexArray", elementIndices);
        channel.Add("m_szChannelClass", "BoneChannel");
        channel.Add("m_szVariableName", variableName);

        return channel;
    }

    private KVObject CreateAnimationData() => CreateAnimationData(animations, segments);

    internal static KVObject CreateAnimationData(KVObject animations, KVObject segments)
    {
        var decoders = KVObject.Array();
        decoders.Add(CreateDecoder("CCompressedFullVector3"));
        decoders.Add(CreateDecoder("CCompressedFullQuaternion"));

        var data = KVObject.Collection();
        data.Add("m_animArray", animations);
        data.Add("m_decoderArray", decoders);
        data.Add("m_segmentArray", segments);

        return data;
    }

    private static KVObject CreateDecoder(string name)
    {
        var decoder = KVObject.Collection();
        decoder.Add("m_szName", name);

        return decoder;
    }

    private KVObject CreateSequenceGroup()
    {
        var sequenceNameArray = KVObject.Array();

        foreach (var name in names)
        {
            sequenceNameArray.Add(name);
        }

        var boneNames = CreateBoneNameArray(GetBoneNames());

        var poseParameters = KVObject.Array();

        foreach (var parameter in source.PoseParameters)
        {
            var poseParameter = KVObject.Collection();
            poseParameter.Add("m_sName", parameter.Name);
            poseParameter.Add("m_flStart", MathF.Min(parameter.Start, parameter.End));
            poseParameter.Add("m_flEnd", MathF.Max(parameter.Start, parameter.End));
            poseParameter.Add("m_flLoop", parameter.Loop);
            poseParameter.Add("m_bLooping", parameter.Loop != 0f);
            poseParameters.Add(poseParameter);
        }

        foreach (var controllerGroup in controllers?.Groups ?? [])
        {
            var poseParameter = KVObject.Collection();
            poseParameter.Add("m_sName", controllerGroup.Name);
            poseParameter.Add("m_flStart", controllerGroup.Low);
            poseParameter.Add("m_flEnd", controllerGroup.High);
            poseParameter.Add("m_flLoop", 0f);
            poseParameter.Add("m_bLooping", false);
            poseParameters.Add(poseParameter);
        }

        var group = KVObject.Collection();
        group.Add("m_localS1SeqDescArray", sequences);
        group.Add("m_localSequenceNameArray", sequenceNameArray);
        group.Add("m_localBoneMaskArray", boneMasks);
        group.Add("m_localBoneNameArray", boneNames);
        group.Add("m_localPoseParamArray", poseParameters);

        return group;
    }
}
