using System.IO;
using System.Linq;
using Datamodel;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    /// <summary>
    /// Gets the list of animations to be extracted with their output file names.
    /// </summary>
    public List<(SequenceAnimation Anim, string FileName)> AnimationsToExtract { get; } = [];

    private readonly HashSet<SequenceAnimation> animationGroupAnimations = [];
    private readonly HashSet<string> animationFileNames = new(StringComparer.OrdinalIgnoreCase);
    private SequenceTables? sequenceTables;
    private Dictionary<string, string>? aliasedSequences;

    /// <summary>
    /// The model's sequence tables, read from its ASEQ block on first use.
    /// </summary>
    private SequenceTables Sequences => sequenceTables ??= ReadSequenceTables();

    /// <summary>
    /// Every sequence that exists only to give a second name to an animation another sequence already
    /// declares, mapped to that animation's own name. Resolved from the sequence tables.
    /// </summary>
    private Dictionary<string, string> AliasedSequences
        => aliasedSequences ??= Sequences.CanResolveReferences
            ? FindAliasedSequences([.. AnimationsToExtract.Where(x => HasOwnAnimFileNode(x.Anim)).Select(x => x.Anim)])
            : [];

    /// <summary>
    /// Whether an animation stands for a node of its own in the model doc, rather than being an
    /// intermediate one that a sequence references.
    /// </summary>
    private bool HasOwnAnimFileNode(SequenceAnimation animation)
        => animation.FromSequence || animationGroupAnimations.Contains(animation);

    /// <summary>
    /// Returns whether a sequence has an animation of its own to write out. A blend has none, nor does
    /// a sequence that only renames one another node already declares.
    /// </summary>
    private bool WritesOwnAnimation(SequenceAnimation animation)
        => !animation.IsBlend && !AliasedSequences.ContainsKey(animation.Name);

    private void EnqueueAnimations()
    {
        if (model == null)
        {
            return;
        }

        foreach (var anim in model.GetEmbeddedAnimations())
        {
            QueueAnimation(anim);
        }

        EnqueueAnimationGroups(model);
    }

    /// <summary>
    /// Queues the animations a model reaches through the standalone animation groups it references.
    /// Those groups are child resources of the model, and come back as its own anim files. A group that
    /// cannot be read is skipped.
    /// </summary>
    private void EnqueueAnimationGroups(Model model)
    {
        if (fileLoader == null)
        {
            return;
        }

        var names = new HashSet<string>(AnimationsToExtract.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var (anim, _) in AnimationsToExtract)
        {
            names.Add(anim.Name);
        }

        foreach (var anim in model.GetAnimationGroupAnimations(fileLoader))
        {
            // Several groups can carry an animation of the same name, and the doc holds one node.
            if (!names.Add(anim.Name))
            {
                continue;
            }

            animationGroupAnimations.Add(anim);
            QueueAnimation(anim);
        }
    }

    private void QueueAnimation(SequenceAnimation anim)
    {
        var fileName = GetDmxFileName_ForAnimation(anim.Name);
        AnimationsToExtract.Add((anim, fileName));
        animationFileNames.Add(fileName);
    }

    private void AddAnimationGraphClips(ContentFile vmdl)
    {
        if (Type != ModelExtractType.Default || model == null || fileLoader == null)
        {
            return;
        }

        foreach (var animation in model.GetAllAnimations(fileLoader))
        {
            if (animation is not ClipAnimation { Clip: var clip })
            {
                continue;
            }

            var clipContent = new NmClipExtract(clip.Resource, fileLoader).ToContentFile();
            clipContent.FileName = animation.Name;
            clipContent.KeepFullPath = true;
            vmdl.AdditionalFiles.Add(clipContent);
        }
    }

    string GetDmxFileName_ForAnimation(string animationName)
    {
        var fileName = ModelName;
        var baseName = (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + Path.GetFileNameWithoutExtension(fileName) // so models in the same directory do not override each other's anims
            + "_"
            + animationName)
            .Replace('\\', '/');

        // A mesh and an animation can share a name, and then one dmx would overwrite the other.
        var candidate = baseName + ".dmx";
        var suffix = 0;

        while (IsDmxFileNameTaken(candidate, includePhysics: false, includeAnimations: true))
        {
            candidate = FormattableString.Invariant($"{baseName}_anim{(suffix > 0 ? suffix : string.Empty)}.dmx");
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// The frame rate to divide frame timings by. Only zero and negative rates are replaced; a
    /// fractional positive rate passes through unclamped.
    /// </summary>
    private static float EffectiveFps(float fps) => fps > 0f ? fps : 1f;

    /// <summary>
    /// Converts an animation to DMX format.
    /// </summary>
    public static byte[] ToDmxAnim(Model model, AnimationBase anim)
        => ToDmxAnim(model.Skeleton, model.FlexControllers, anim);

    /// <summary>
    /// Converts an animation to DMX format using skeleton and flex controllers.
    /// </summary>
    public static byte[] ToDmxAnim(Skeleton skeleton, FlexController[] flexControllers, AnimationBase anim, bool nmSkelAxisFixup = false)
        => ToDmxAnim(skeleton, flexControllers, anim, [], nmSkelAxisFixup);

    /// <summary>
    /// Converts an animation to DMX format using skeleton and flex controllers. Secondary animations
    /// (an NM clip's tracks for further skeletons, e.g. the weapon of a viewmodel clip) are written
    /// into the same DMX, their joints appended beside the primary skeleton's.
    /// </summary>
    public static byte[] ToDmxAnim(Skeleton skeleton, FlexController[] flexControllers, AnimationBase anim,
        IReadOnlyList<(Skeleton Skeleton, AnimationBase Animation)> secondaryAnimations, bool nmSkelAxisFixup = false)
        => ToDmxAnim(skeleton, flexControllers, anim, secondaryAnimations, nmSkelAxisFixup, keepClothProxyBones: true);

    /// <summary>
    /// Converts an animation to DMX format. Generated cloth proxy bones are written as ordinary joints only when
    /// <paramref name="keepClothProxyBones"/> is set.
    /// </summary>
    internal static byte[] ToDmxAnim(Skeleton skeleton, FlexController[] flexControllers, AnimationBase anim,
        IReadOnlyList<(Skeleton Skeleton, AnimationBase Animation)> secondaryAnimations, bool nmSkelAxisFixup, bool keepClothProxyBones)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);

        // The frames below get the axis fixup, so the bind pose it is written against has to have it too
        var dmeSkeleton = BuildDmeDagSkeleton(skeleton, out var transforms, nmSkelAxisFixup, keepClothProxyBones: keepClothProxyBones);

        var animationList = new DmeAnimationList();
        var clip = new DmeChannelsClip
        {
            FrameRate = anim.Fps
        };

        var fps = EffectiveFps(anim.Fps);

        if (anim.FrameCount > 0)
        {
            clip.TimeFrame.Duration = TimeSpan.FromSeconds((double)(anim.FrameCount - 1) / fps);

            var tracks = DecodeTracks(skeleton, flexControllers, anim, nmSkelAxisFixup, keepClothProxyBones);
            var times = GetFrameTimes(anim);

            ProcessRootMotionChannel(anim, dmeSkeleton, clip);
            ProcessBoneChannels(skeleton, transforms, clip, tracks, times);
            ProcessFlexChannels(flexControllers, clip, tracks, times);
        }

        foreach (var (secondarySkeleton, secondaryAnimation) in secondaryAnimations)
        {
            if (secondaryAnimation.FrameCount == 0)
            {
                continue;
            }

            var secondaryTransforms = AppendDmeSkeletonJoints(dmeSkeleton, secondarySkeleton, keepClothProxyBones: keepClothProxyBones);
            var secondaryTracks = DecodeTracks(secondarySkeleton, [], secondaryAnimation, nmSkelAxisFixup: false, keepClothProxyBones);

            ProcessBoneChannels(secondarySkeleton, secondaryTransforms, clip, secondaryTracks, GetFrameTimes(secondaryAnimation));
        }

        animationList.Animations.Add(clip);

        using var stream = new MemoryStream();

        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeSkeleton,
            ["animationList"] = animationList,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["app"] = "sfm", //modeldoc won't import dmx animations without this
                ["source"] = $"Generated with {StringToken.VRF_GENERATOR}",
            }
        };

        dmx.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    private static DmeChannel BuildDmeChannel(string name, Element toElement, string toAttribute, DmeLog log, DmeLogLayer layer)
    {
        log.Layers.Add(layer);

        return new DmeChannel
        {
            Name = name,
            ToElement = toElement,
            ToAttribute = toAttribute,
            Mode = 3,
            Log = log
        };
    }

    /// <summary>
    /// Every frame of an animation, laid out as the log layer values of each bone and flex controller.
    /// Bones that are not exported have no values.
    /// </summary>
    private readonly record struct DecodedTracks(Vector3[]?[] Positions, Quaternion[]?[] Orientations, float[][] FlexValues);

    private static DecodedTracks DecodeTracks(Skeleton skeleton, FlexController[] flexControllers, AnimationBase anim, bool nmSkelAxisFixup, bool keepClothProxyBones)
    {
        var frameCount = anim.FrameCount;
        var bones = skeleton.Bones;
        var rootMotionBone = skeleton["root_motion"];

        var positions = new Vector3[]?[bones.Length];
        var orientations = new Quaternion[]?[bones.Length];
        var flexValues = new float[flexControllers.Length][];

        foreach (var bone in bones)
        {
            if (keepClothProxyBones || !ClothBones.IsGeneratedProxyBone(bone))
            {
                positions[bone.Index] = new Vector3[frameCount];
                orientations[bone.Index] = new Quaternion[frameCount];
            }
        }

        for (var flexId = 0; flexId < flexValues.Length; flexId++)
        {
            flexValues[flexId] = new float[frameCount];
        }

        var frame = new Frame(skeleton, flexControllers);

        for (var i = 0; i < frameCount; i++)
        {
            frame.Clear(skeleton);
            frame.FrameIndex = i;
            anim.DecodeFrame(frame);

            if (nmSkelAxisFixup && rootMotionBone != null)
            {
                frame.Bones[rootMotionBone.Index].Angle = NmAxisFixupRootMotion(frame.Bones[rootMotionBone.Index].Angle);

                foreach (var root in rootMotionBone.Children)
                {
                    (frame.Bones[root.Index].Position, frame.Bones[root.Index].Angle)
                        = NmAxisFixupChild(frame.Bones[root.Index].Position, frame.Bones[root.Index].Angle);
                }
            }

            foreach (var bone in bones)
            {
                var bonePositions = positions[bone.Index];
                var boneOrientations = orientations[bone.Index];

                if (bonePositions == null || boneOrientations == null)
                {
                    continue;
                }

                var frameBone = frame.Bones[bone.Index];

                var position = frameBone.Position;
                if (bone == rootMotionBone)
                {
                    // vertical root motion is not applied to the visible body. baking it floats the model up.
                    position.Z = 0f;
                }

                bonePositions[i] = position;
                boneOrientations[i] = frameBone.Angle;
            }

            for (var flexId = 0; flexId < flexValues.Length; flexId++)
            {
                flexValues[flexId][i] = frame.Datas[flexId];
            }
        }

        return new DecodedTracks(positions, orientations, flexValues);
    }

    private static TimeSpan[] GetFrameTimes(AnimationBase anim)
    {
        var times = new TimeSpan[anim.FrameCount];

        for (var i = 0; i < times.Length; i++)
        {
            times[i] = TimeSpan.FromSeconds((double)i / EffectiveFps(anim.Fps));
        }

        return times;
    }

    /// <summary>
    /// Whether the animation's root motion should be written out for recompiling. All-zero movement
    /// is skipped, since extracting motion from it on recompile would turn the root bone's sway into
    /// real movement.
    /// </summary>
    private static bool ShouldExtractMotion(AnimationBase anim)
        => anim.HasMovementData() && (anim is not SequenceAnimation sequence || sequence.HasNonZeroMovementData());

    private static void ProcessRootMotionChannel(AnimationBase anim, DmeModel skeleton, DmeChannelsClip clip)
    {
        if (!ShouldExtractMotion(anim))
        {
            return;
        }

        var rootPositionLayer = new DmeVector3LogLayer { LayerValues = new Vector3[anim.FrameCount] };
        var rootPositionChannel = BuildDmeChannel("_p", skeleton.Transform, "position", new DmeVector3Log(), rootPositionLayer);

        var rootOrientationLayer = new DmeQuaternionLogLayer { LayerValues = new Quaternion[anim.FrameCount] };
        var rootOrientationChannel = BuildDmeChannel("_o", skeleton.Transform, "orientation", new DmeQuaternionLog(), rootOrientationLayer);

        for (var i = 0; i < anim.FrameCount; i++)
        {
            var time = i / EffectiveFps(anim.Fps);
            var timespan = TimeSpan.FromSeconds(time);

            var movement = anim.GetMovementOffsetData(time);

            // vertical root motion is not applied to the visible body, so don't bake it.
            rootPositionLayer.LayerValues[i] = new Vector3(movement.Position.X, movement.Position.Y, 0f);
            rootPositionLayer.Times.Add(timespan);

            var radians = float.DegreesToRadians(movement.Angle);
            rootOrientationLayer.LayerValues[i] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians);
            rootOrientationLayer.Times.Add(timespan);
        }

        ApplyModelDocHack(rootPositionLayer);

        clip.Channels.Add(rootPositionChannel);
        clip.Channels.Add(rootOrientationChannel);
    }

    private static void ProcessFlexChannels(FlexController[] flexControllers, DmeChannelsClip clip, DecodedTracks tracks, TimeSpan[] times)
    {
        for (var flexId = 0; flexId < flexControllers.Length; flexId++)
        {
            var flexController = flexControllers[flexId];

            var flexElement = new Element
            {
                Name = flexController.Name
            };
            flexElement.Add("flexWeight", 0f);

            var flexLogLayer = new DmeFloatLogLayer { LayerValues = tracks.FlexValues[flexId] };
            flexLogLayer.Times.AddRange(times);

            var flexChannel = BuildDmeChannel($"{flexController.Name}_flex_channel", flexElement, "flexWeight", new DmeFloatLog(), flexLogLayer);
            clip.Channels.Add(flexChannel);
        }
    }

    private static void ProcessBoneChannels(Skeleton skeleton, DmeTransform[] transforms, DmeChannelsClip clip, DecodedTracks tracks, TimeSpan[] times)
    {
        foreach (var bone in skeleton.Bones)
        {
            var positions = tracks.Positions[bone.Index];
            var orientations = tracks.Orientations[bone.Index];

            if (positions == null || orientations == null)
            {
                continue;
            }

            var transform = transforms[bone.Index];
            var boneName = GetExportBoneName(bone);

            var positionLogLayer = new DmeVector3LogLayer { LayerValues = positions };
            var orientationLogLayer = new DmeQuaternionLogLayer { LayerValues = orientations };
            positionLogLayer.Times.AddRange(times);
            orientationLogLayer.Times.AddRange(times);

            var positionChannel = BuildDmeChannel($"{boneName}_p", transform, "position", new DmeVector3Log(), positionLogLayer);
            var orientationChannel = BuildDmeChannel($"{boneName}_o", transform, "orientation", new DmeQuaternionLog(), orientationLogLayer);

            ApplyModelDocHack(positionLogLayer);

            clip.Channels.Add(positionChannel);
            clip.Channels.Add(orientationChannel);
        }
    }

    /// <summary>
    /// Workaround for ModelDoc ignoring animation data on bone when bone doesn't have any motion
    /// </summary>
    private static void ApplyModelDocHack(DmeVector3LogLayer logLayer)
    {
        // I guess this means there is actually no animation data?
        if (logLayer.LayerValues.Length == 0)
        {
            return;
        }

        if (DoesLayerHaveMotion(logLayer))
        {
            return;
        }

        var newLayerValues = new Vector3[logLayer.LayerValues.Length + 2];
        var newTimes = new TimeSpanArray(newLayerValues.Length);

        var baseValue = logLayer.LayerValues[0];

        newLayerValues[0] = baseValue + new Vector3(0, 0, 0.0001f);
        newLayerValues[1] = baseValue;
        newTimes.Add(TimeSpan.FromSeconds(-0.1f));
        newTimes.Add(TimeSpan.FromSeconds(-0.05f));
        for (var i = 0; i < logLayer.LayerValues.Length; i++)
        {
            newLayerValues[i + 2] = logLayer.LayerValues[i];
            newTimes.Add(logLayer.Times[i]);
        }

        logLayer.LayerValues = newLayerValues;
        logLayer.Times.Clear();
        logLayer.Times.AddRange(newTimes);
    }

    private static bool DoesLayerHaveMotion(DmeVector3LogLayer logLayer)
    {
        if (logLayer.LayerValues.Length == 1)
        {
            return false;
        }

        var lastVal = logLayer.LayerValues[0];
        for (var i = 1; i < logLayer.LayerValues.Length; i++)
        {
            var currentVal = logLayer.LayerValues[i];

            if (Vector3.Distance(lastVal, currentVal) >= 0.01f)
            {
                return true;
            }

            lastVal = currentVal;
        }

        return false;
    }
}
