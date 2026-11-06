using System.Globalization;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Source1.Model;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// Bakes the sequences of a GoldSrc model into the embedded animation data of a Source 2 model.
/// Each animation of a sequence is sampled at every frame with the bone controllers at their defaults. A sequence with
/// several animations blends them along one axis, which becomes a pose parameter named after what drives the axis.
/// Every bone controller becomes a pose parameter that weights delta layers, which move its bones from the sampled pose.
/// </summary>
internal sealed class GoldSrcAnimationBaker
{
    // A sequence has two blend axes, of which models use the first
    private const int BlendAxis = 0;

    // Controllers past the first four are driven by the mouth
    private const int MaxPlainControllerIndex = 3;

    private sealed class BlendParameter(string name, float start, float end)
    {
        public string Name { get; } = name;
        public float Start { get; set; } = start;
        public float End { get; set; } = end;
    }

    private readonly GoldSrcMdlFile source;
    private readonly List<string> names = [];
    private readonly ModelNameSet usedNames = new();
    private readonly List<BlendParameter> parameters = [];
    private readonly Dictionary<int, int> parameterIndices = [];
    private readonly KVObject animations = KVObject.Array();
    private readonly KVObject segments = KVObject.Array();
    private readonly KVObject sequences = KVObject.Array();

    private readonly Dictionary<int, int> controllerParameterIndices = [];
    private readonly int[][] controllerBones;

    private readonly string?[] sequenceNames;

    private GoldSrcAnimationBaker(GoldSrcMdlFile source)
    {
        this.source = source;
        sequenceNames = new string?[source.Sequences.Length];
        controllerBones = GetControllerBones(source);
    }

    // The bones each bone controller moves, which is none for a controller that has no range
    private static int[][] GetControllerBones(GoldSrcMdlFile source)
    {
        var controllerBones = new int[source.BoneControllers.Length][];

        for (var controller = 0; controller < controllerBones.Length; controller++)
        {
            var bones = new List<int>();
            var boneController = source.BoneControllers[controller];

            for (var bone = 0; bone < source.Bones.Length; bone++)
            {
                if (boneController.Start != boneController.End && Array.IndexOf(source.Bones[bone].Controllers, controller) >= 0)
                {
                    bones.Add(bone);
                }
            }

            controllerBones[controller] = [.. bones];
        }

        return controllerBones;
    }

    /// <summary>Bakes the sequences of a model that have all their data.</summary>
    /// <param name="source">The model to bake the sequences of, with its sequence group files loaded.</param>
    /// <param name="sequenceNames">Receives the name the baked sequence of each sequence of the model has, or <see langword="null"/> for one that was left out.</param>
    /// <returns>The animation data, or <see langword="null"/> when the model has no sequence that can be played.</returns>
    public static ModelAnimationSource? Bake(GoldSrcMdlFile source, out string?[] sequenceNames)
    {
        var baker = new GoldSrcAnimationBaker(source);
        var animations = baker.Bake();
        sequenceNames = baker.sequenceNames;

        return animations;
    }

    private ModelAnimationSource? Bake()
    {
        for (var i = 0; i < source.Sequences.Length; i++)
        {
            var sequence = source.Sequences[i];

            if (source.HasAnimationData(sequence))
            {
                sequenceNames[i] = AddSequence(sequence);
            }
        }

        if (sequences.Count == 0)
        {
            return null;
        }

        var boneNames = new List<string>(source.Bones.Length);

        foreach (var bone in source.Bones)
        {
            boneNames.Add(bone.ExportName);
        }

        var group = Source1AnimationBaker.CreateAnimationGroup(boneNames);
        var data = Source1AnimationBaker.CreateAnimationData(animations, segments);

        return new ModelAnimationSource(group, data, CreateSequenceGroup(boneNames));
    }

    // Returns the name of the baked sequence, or null when its animation data is damaged
    private string? AddSequence(GoldSrcSequence sequence)
    {
        var frameCount = Math.Max(1, sequence.FrameCount);
        var blendType = sequence.BlendType[BlendAxis];
        var isDriven = sequence.BlendCount > 1 && blendType != 0;
        var animationCount = isDriven ? sequence.BlendCount : 1;
        var frames = new List<(Vector3[] Positions, Quaternion[] Rotations)>(animationCount);

        try
        {
            for (var i = 0; i < animationCount; i++)
            {
                frames.Add(SampleAnimation(sequence, i, frameCount));
            }
        }
        catch (InvalidDataException)
        {
            // A sequence with damaged animation data is left out instead of failing the whole model
            return null;
        }

        var name = usedNames.Create(sequence.Label);
        var sequenceNameIndex = names.Count;
        names.Add(name);

        var references = KVObject.Array();

        for (var i = 0; i < animationCount; i++)
        {
            var animationName = name;

            if (isDriven)
            {
                animationName = usedNames.Create(string.Create(CultureInfo.InvariantCulture, $"{name}_blend{i}"));
                references.Add(names.Count);
                names.Add(animationName);
            }
            else
            {
                references.Add(sequenceNameIndex);
            }

            var firstSegment = Source1AnimationBaker.AddSegments(segments, source.Bones.Length, frames[i].Positions, frames[i].Rotations);
            var events = i == 0 ? CreateEvents(sequence, frameCount) : KVObject.Array();

            animations.Add(Source1AnimationBaker.CreateAnimation(
                animationName, sequence.Fps, frameCount, firstSegment, sequence.IsLooping, isDriven, false, KVObject.Array(), events));
        }

        var layers = AddControllerLayers(sequence, name, frames[0].Positions, frames[0].Rotations, frameCount);
        sequences.Add(Source1AnimationBaker.CreateSequenceDescription(
            name, CreateSequenceFlags(sequence), CreateActivities(sequence), KVObject.Collection(), layers, CreateFetch(sequence, isDriven, references), 0));

        return name;
    }

    // Each bone controller adds to the pose of the bones it moves, by a layer for each way it can move from its default
    // that is weighted by how far the pose parameter of the controller is that way
    private KVObject AddControllerLayers(GoldSrcSequence sequence, string name, Vector3[] restPositions, Quaternion[] restRotations, int frameCount)
    {
        var layers = KVObject.Array();

        for (var controller = 0; controller < controllerBones.Length; controller++)
        {
            var bones = controllerBones[controller];

            if (bones.Length == 0)
            {
                continue;
            }

            var boneController = source.BoneControllers[controller];
            var low = MathF.Min(boneController.Start, boneController.End);
            var high = MathF.Max(boneController.Start, boneController.End);
            var rest = source.GetDefaultControllerValue(controller);
            var parameter = GetControllerParameter(controller, low, high);

            // The ranges leave a margin so that the weight stays at one up to the end of the parameter
            if (high > rest)
            {
                var layerName = usedNames.Create(string.Create(CultureInfo.InvariantCulture, $"{name}_controller{controller}_up"));
                var reference = AddControllerDelta(sequence, layerName, restPositions, restRotations, frameCount, controller, bones, high);
                layers.Add(Source1AnimationBaker.CreateControllerLayer(reference, parameter, rest, high, high + 1f, high + 1f));
            }

            if (low < rest)
            {
                var layerName = usedNames.Create(string.Create(CultureInfo.InvariantCulture, $"{name}_controller{controller}_down"));
                var reference = AddControllerDelta(sequence, layerName, restPositions, restRotations, frameCount, controller, bones, low);
                layers.Add(Source1AnimationBaker.CreateControllerLayer(reference, parameter, low - 1f, low, low, rest));
            }
        }

        return layers;
    }

    // Bakes how far the bones of a controller move from the first animation of the sequence when it is at a value, as a hidden delta sequence
    private int AddControllerDelta(GoldSrcSequence sequence, string name, Vector3[] restPositions, Quaternion[] restRotations, int frameCount, int controller, int[] bones, float value)
    {
        var boneCount = source.Bones.Length;
        var values = new float[source.BoneControllers.Length];

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = source.GetDefaultControllerValue(i);
        }

        values[controller] = value;

        var positions = new Vector3[frameCount * bones.Length];
        var rotations = new Quaternion[frameCount * bones.Length];
        var posePositions = new Vector3[boneCount];
        var poseRotations = new Quaternion[boneCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            source.GetAnimationPose(sequence, 0, frame, values, posePositions, poseRotations);

            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                var restIndex = (frame * boneCount) + bone;
                var delta = Quaternion.Normalize(Quaternion.Inverse(restRotations[restIndex]) * poseRotations[bone]);

                // Both signs are the same rotation, and blending goes the short way round
                positions[(frame * bones.Length) + i] = posePositions[bone] - restPositions[restIndex];
                rotations[(frame * bones.Length) + i] = delta.W < 0f ? Quaternion.Negate(delta) : delta;
            }
        }

        var nameIndex = names.Count;
        names.Add(name);

        var firstSegment = Source1AnimationBaker.AddSegments(segments, bones, positions, rotations);
        animations.Add(Source1AnimationBaker.CreateAnimation(
            name, sequence.Fps, frameCount, firstSegment, sequence.IsLooping, true, true, KVObject.Array(), KVObject.Array()));

        sequences.Add(Source1AnimationBaker.CreateControllerDeltaSequence(name, sequence.IsLooping, nameIndex));

        return nameIndex;
    }

    // Controllers have a pose parameter of their own, which the blends of sequences with the same kind of type do not share
    private int GetControllerParameter(int controller, float low, float high)
    {
        if (controllerParameterIndices.TryGetValue(controller, out var index))
        {
            return index;
        }

        var boneController = source.BoneControllers[controller];
        var name = boneController.Index > MaxPlainControllerIndex
            ? "mouth_controller"
            : string.Create(CultureInfo.InvariantCulture, $"controller{boneController.Index}");

        index = parameters.Count;
        parameters.Add(new BlendParameter(name, low, high));
        controllerParameterIndices[controller] = index;

        return index;
    }

    private (Vector3[] Positions, Quaternion[] Rotations) SampleAnimation(GoldSrcSequence sequence, int animation, int frameCount)
    {
        var boneCount = source.Bones.Length;
        var positions = new Vector3[frameCount * boneCount];
        var rotations = new Quaternion[frameCount * boneCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            var framePositions = positions.AsSpan(frame * boneCount, boneCount);
            var frameRotations = rotations.AsSpan(frame * boneCount, boneCount);

            source.GetAnimationPose(sequence, animation, frame, [], framePositions, frameRotations);
        }

        return (positions, rotations);
    }

    private static KVObject CreateSequenceFlags(GoldSrcSequence sequence)
    {
        return Source1AnimationBaker.CreateSequenceFlags(sequence.IsLooping, false, false, false, false, false);
    }

    private static KVObject CreateActivities(GoldSrcSequence sequence)
    {
        var activities = KVObject.Array();

        if (sequence.Activity == 0)
        {
            return activities;
        }

        var activity = KVObject.Collection();
        activity.Add("m_name", sequence.Activity.ToString(CultureInfo.InvariantCulture));
        activity.Add("m_nActivity", sequence.Activity);
        activity.Add("m_nFlags", 0);
        activity.Add("m_nWeight", sequence.ActivityWeight);
        activities.Add(activity);

        return activities;
    }

    private KVObject CreateFetch(GoldSrcSequence sequence, bool isDriven, KVObject references)
    {
        var flags = KVObject.Collection();
        var localPose = KVObject.Array();
        var keys = KVObject.Array();
        var groupSize = KVObject.Array();

        if (isDriven)
        {
            var start = sequence.BlendStart[BlendAxis];
            var end = sequence.BlendEnd[BlendAxis];
            var count = sequence.BlendCount;

            flags.Add("m_b1D", true);
            localPose.Add(GetParameterIndex(sequence.BlendType[BlendAxis], start, end));
            groupSize.Add(count);

            // The animations are spread evenly from the start value of the axis to its end value
            for (var i = 0; i < count; i++)
            {
                var fraction = (float)i / (count - 1);
                keys.Add(start + ((end - start) * fraction));
            }
        }

        var fetch = Source1AnimationBaker.CreateFetch(flags, localPose, keys, KVObject.Array(), groupSize, -1);
        fetch.Add("m_localReferenceArray", references);

        return fetch;
    }

    // Sequences driven by the same kind of controller share a pose parameter that covers all their ranges
    private int GetParameterIndex(int blendType, float start, float end)
    {
        var low = MathF.Min(start, end);
        var high = MathF.Max(start, end);

        if (parameterIndices.TryGetValue(blendType, out var index))
        {
            var parameter = parameters[index];
            parameter.Start = MathF.Min(parameter.Start, low);
            parameter.End = MathF.Max(parameter.End, high);

            return index;
        }

        index = parameters.Count;
        parameters.Add(new BlendParameter(GetBlendName(blendType), low, high));
        parameterIndices[blendType] = index;

        return index;
    }

    // The channels a bone controller moves, by the bit of its type
    private static string GetBlendName(int blendType) => blendType switch
    {
        0x01 => "x",
        0x02 => "y",
        0x04 => "z",
        0x08 => "xr",
        0x10 => "yr",
        0x20 => "zr",
        0x40 => "mouth",
        _ => string.Create(CultureInfo.InvariantCulture, $"blend{blendType}"),
    };

    private static KVObject CreateEvents(GoldSrcSequence sequence, int frameCount)
    {
        var events = KVObject.Array();
        var lastFrame = frameCount - 1;

        foreach (var sequenceEvent in sequence.Events)
        {
            var cycle = lastFrame > 0 ? (float)sequenceEvent.Frame / lastFrame : 0f;

            var isSoundId = sequenceEvent.Event is Source1AnimationBaker.ClientSoundEventId or Source1AnimationBaker.ScriptSoundEventId or Source1AnimationBaker.ScriptVoiceSoundEventId;
            var isSound = isSoundId && sequenceEvent.Options.Length > 0;
            var eventName = isSound ? Source1AnimationBaker.PlaySoundEventName : sequenceEvent.Event.ToString(CultureInfo.InvariantCulture);

            var data = KVObject.Collection();
            data.Add("m_sEventName", eventName);
            data.Add("m_nFrame", sequenceEvent.Frame);
            data.Add("m_flCycle", cycle);
            data.Add("m_sOptions", sequenceEvent.Options);

            if (isSound)
            {
                data.Add("m_EventData", Source1AnimationBaker.CreateSoundEventData(sequenceEvent.Options));
            }

            events.Add(data);
        }

        return events;
    }

    private KVObject CreateSequenceGroup(List<string> boneNames)
    {
        var sequenceNameArray = KVObject.Array();

        foreach (var name in names)
        {
            sequenceNameArray.Add(name);
        }

        var boneNameArray = Source1AnimationBaker.CreateBoneNameArray(boneNames);

        var poseParameters = KVObject.Array();

        foreach (var parameter in parameters)
        {
            var poseParameter = KVObject.Collection();
            poseParameter.Add("m_sName", parameter.Name);
            poseParameter.Add("m_flStart", parameter.Start);
            poseParameter.Add("m_flEnd", parameter.End);
            poseParameter.Add("m_flLoop", 0f);
            poseParameter.Add("m_bLooping", false);
            poseParameters.Add(poseParameter);
        }

        // The mask at index zero stands for a sequence that has no weight list
        var boneMasks = KVObject.Array();
        boneMasks.Add(Source1AnimationBaker.CreateBoneMask("default", []));

        var group = KVObject.Collection();
        group.Add("m_localS1SeqDescArray", sequences);
        group.Add("m_localSequenceNameArray", sequenceNameArray);
        group.Add("m_localBoneMaskArray", boneMasks);
        group.Add("m_localBoneNameArray", boneNameArray);
        group.Add("m_localPoseParamArray", poseParameters);

        return group;
    }
}
