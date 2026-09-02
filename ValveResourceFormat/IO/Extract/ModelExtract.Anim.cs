using System.Linq;
﻿using System.IO;
using Datamodel;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    /// <summary>
    /// Gets the list of animations to be extracted with their output file names.
    /// </summary>
    public List<(SequenceAnimation Anim, string FileName)> AnimationsToExtract { get; } = [];

    private readonly HashSet<SequenceAnimation> animationGroupAnimations = [];
    private SequenceTables? sequenceTables;
    private Dictionary<string, string>? aliasedSequences;

    /// <summary>
    /// The model's sequence tables, read from its ASEQ block on first use.
    /// </summary>
    private SequenceTables Sequences => sequenceTables ??= ReadSequenceTables();

    /// <summary>
    /// Every sequence that exists only to give a second name to an animation another sequence already
    /// declares, mapped to that animation's own name. Resolved from the sequence tables, so it does
    /// not depend on <see cref="ToValveModel"/> having run.
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
    /// Returns whether a sequence has an animation of its own to write out. A blend has none, it is
    /// rebuilt as a node listing the ones it blends, and neither has a sequence that only renames one
    /// another node already declares.
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
            AnimationsToExtract.Add((anim, GetDmxFileName_ForAnimation(anim.Name)));
        }

        EnqueueAnimationGroups(model);
    }

    /// <summary>
    /// Queues the animations a model reaches through the standalone animation groups it references.
    /// The compiler writes those groups as child resources of the model, so their animations were
    /// authored as the model's own anim files and come back as such. A group that cannot be read is
    /// skipped: it is another resource, and the model itself still extracts without it.
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

        List<SequenceAnimation> animations;

        try
        {
            animations = [.. model.GetAnimationGroupAnimations(fileLoader)];
        }
        catch (Exception e)
        {
            ProgressReporter?.Report($"Skipping animation group animations: {e.Message}");
            return;
        }

        foreach (var anim in animations)
        {
            // Several groups can carry an animation of the same name, and the doc can hold only one
            // node under it.
            if (!names.Add(anim.Name))
            {
                continue;
            }

            animationGroupAnimations.Add(anim);
            AnimationsToExtract.Add((anim, GetDmxFileName_ForAnimation(anim.Name)));
        }
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

            try
            {
                var clipContent = new NmClipExtract(clip.Resource, fileLoader).ToContentFile();
                clipContent.FileName = animation.Name;
                clipContent.KeepFullPath = true;
                vmdl.AdditionalFiles.Add(clipContent);
            }
            catch (Exception e)
            {
                // A single malformed clip shouldn't fail the whole model export.
                ProgressReporter?.Report($"Skipping animation graph clip '{animation.Name}': {e.Message}");
            }
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

        while (RenderMeshesToExtract.Exists(m => m.FileName == candidate)
            || AnimationsToExtract.Exists(a => a.FileName == candidate))
        {
            candidate = FormattableString.Invariant($"{baseName}_anim{(suffix > 0 ? suffix : string.Empty)}.dmx");
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// The frame rate to divide frame timings by. Guards only against zero/negative; a fractional but
    /// positive authored rate (some looping "held pose" sequences compile with fps under 1) must pass
    /// through unclamped or the exported clip's duration comes out far shorter than its real length.
    /// </summary>
    private static float EffectiveFps(float fps) => fps > 0f ? fps : 1f;

    /// <summary>
    /// Converts an animation to DMX format.
    /// </summary>
    public static byte[] ToDmxAnim(Model model, Animation anim)
        => ToDmxAnim(model.Skeleton, model.FlexControllers, anim);

    /// <summary>
    /// Converts an animation to DMX format using skeleton and flex controllers.
    /// </summary>
    public static byte[] ToDmxAnim(Skeleton skeleton, FlexController[] flexControllers, Animation anim, bool nmSkelAxisFixup = false)
        => ToDmxAnim(skeleton, flexControllers, anim, [], nmSkelAxisFixup);

    /// <summary>
    /// Converts an animation to DMX format using skeleton and flex controllers. Secondary animations
    /// (an NM clip's tracks for further skeletons, e.g. the weapon of a viewmodel clip) are written
    /// into the same DMX, their joints appended beside the primary skeleton's.
    /// </summary>
    public static byte[] ToDmxAnim(Skeleton skeleton, FlexController[] flexControllers, Animation anim,
        IReadOnlyList<(Skeleton Skeleton, Animation Animation)> secondaryAnimations, bool nmSkelAxisFixup = false)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);

        var rootMotionBone = skeleton["root_motion"];

        // The frames below get the axis fixup, so the bind pose it is written against has to have it too
        var dmeSkeleton = BuildDmeDagSkeleton(skeleton, out var transforms, nmSkelAxisFixup);

        var animationList = new DmeAnimationList();
        var clip = new DmeChannelsClip
        {
            FrameRate = anim.Fps
        };

        var fps = EffectiveFps(anim.Fps);

        if (anim.FrameCount > 0)
        {
            clip.TimeFrame.Duration = TimeSpan.FromSeconds((double)(anim.FrameCount - 1) / fps);

            var frames = new Frame[anim.FrameCount];
            for (var i = 0; i < anim.FrameCount; i++)
            {
                var frame = new Frame(skeleton, flexControllers)
                {
                    FrameIndex = i
                };
                anim.DecodeFrame(frame);
                frames[i] = frame;

                if (nmSkelAxisFixup && rootMotionBone != null)
                {
                    frame.Bones[rootMotionBone.Index].Angle = NmAxisFixupRootMotion(frame.Bones[rootMotionBone.Index].Angle);

                    foreach (var root in rootMotionBone.Children)
                    {
                        (frame.Bones[root.Index].Position, frame.Bones[root.Index].Angle)
                            = NmAxisFixupChild(frame.Bones[root.Index].Position, frame.Bones[root.Index].Angle);
                    }
                }
            }

            ProcessRootMotionChannel(anim, dmeSkeleton, clip);
            ProcessBoneChannels(skeleton, anim, transforms, clip, frames);
            ProcessFlexChannels(flexControllers, anim, clip, frames);
        }

        foreach (var (secondarySkeleton, secondaryAnimation) in secondaryAnimations)
        {
            if (secondaryAnimation.FrameCount == 0)
            {
                continue;
            }

            var secondaryTransforms = AppendDmeSkeletonJoints(dmeSkeleton, secondarySkeleton);

            var secondaryFrames = new Frame[secondaryAnimation.FrameCount];
            for (var i = 0; i < secondaryAnimation.FrameCount; i++)
            {
                var frame = new Frame(secondarySkeleton, [])
                {
                    FrameIndex = i
                };
                secondaryAnimation.DecodeFrame(frame);
                secondaryFrames[i] = frame;
            }

            ProcessBoneChannels(secondarySkeleton, secondaryAnimation, secondaryTransforms, clip, secondaryFrames);
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

    private static DmeChannel BuildDmeChannel<T>(string name, Element toElement, string toAttribute, out DmeLog<T> log)
    {
        log = [];

        var channel = new DmeChannel
        {
            Name = name,
            ToElement = toElement,
            ToAttribute = toAttribute,
            Mode = 3,
            Log = log
        };

        log.AddLayer(new DmeLogLayer<T>());

        return channel;
    }

    private static void ProcessBoneFrameForDmeChannel(Bone bone, Frame frame, TimeSpan time, DmeLogLayer<Vector3> positionLayer, DmeLogLayer<Quaternion> orientationLayer, bool dropVertical)
    {
        var frameBone = frame.Bones[bone.Index];

        var position = frameBone.Position;
        if (dropVertical)
        {
            // vertical root motion is not applied to the visible body. baking it floats the model up.
            position.Z = 0f;
        }

        positionLayer.Times.Add(time);
        positionLayer.LayerValues[frame.FrameIndex] = position;

        orientationLayer.Times.Add(time);
        orientationLayer.LayerValues[frame.FrameIndex] = frameBone.Angle;
    }

    private static void ProcessFlexFrameForDmeChannel(int flexId, Frame frame, TimeSpan time, DmeLogLayer<float> flexLayer)
    {
        var flexValue = frame.Datas[flexId];

        flexLayer.Times.Add(time);
        flexLayer.LayerValues[frame.FrameIndex] = flexValue;
    }

    private static void ProcessRootMotionChannel(Animation anim, DmeModel skeleton, DmeChannelsClip clip)
    {
        if (!anim.HasMovementData())
        {
            return;
        }
        var rootPositionChannel = BuildDmeChannel<Vector3>($"_p", skeleton.Transform, "position", out var rootPositionLog);
        var rootPositionLayer = rootPositionLog.GetLayer(0);
        rootPositionLayer.LayerValues = new Vector3[anim.FrameCount];

        var rootOrientationChannel = BuildDmeChannel<Quaternion>($"_o", skeleton.Transform, "orientation", out var rootOrientationLog);
        var rootOrientationLayer = rootOrientationLog.GetLayer(0);
        rootOrientationLayer.LayerValues = new Quaternion[anim.FrameCount];

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

    private static void ProcessFlexChannels(FlexController[] flexControllers, Animation anim, DmeChannelsClip clip, Frame[] frames)
    {
        for (var flexId = 0; flexId < flexControllers.Length; flexId++)
        {
            var flexController = flexControllers[flexId];

            var flexElement = new Element
            {
                Name = flexController.Name
            };
            flexElement.Add("flexWeight", 0f);

            var flexChannel = BuildDmeChannel<float>($"{flexController.Name}_flex_channel", flexElement, "flexWeight", out var flexLog);
            var flexLogLayer = flexLog.GetLayer(0);
            flexLogLayer.LayerValues = new float[anim.FrameCount];

            for (var i = 0; i < anim.FrameCount; i++)
            {
                var frame = frames[i];
                var time = TimeSpan.FromSeconds((double)i / EffectiveFps(anim.Fps));
                ProcessFlexFrameForDmeChannel(flexId, frame, time, flexLogLayer);
            }
            clip.Channels.Add(flexChannel);
        }
    }

    private static void ProcessBoneChannels(Skeleton skeleton, Animation anim, DmeTransform[] transforms, DmeChannelsClip clip, Frame[] frames)
    {
        var rootMotionBone = skeleton["root_motion"];

        foreach (var bone in skeleton.Bones)
        {
            var transform = transforms[bone.Index];
            var boneName = GetExportBoneName(bone);

            var positionChannel = BuildDmeChannel<Vector3>($"{boneName}_p", transform, "position", out var positionLog);
            var orientationChannel = BuildDmeChannel<Quaternion>($"{boneName}_o", transform, "orientation", out var orientationLog);

            var positionLogLayer = positionLog.GetLayer(0);
            var orientationLogLayer = orientationLog.GetLayer(0);

            positionLogLayer.LayerValues = new Vector3[anim.FrameCount];
            orientationLogLayer.LayerValues = new Quaternion[anim.FrameCount];

            for (var i = 0; i < anim.FrameCount; i++)
            {
                var frame = frames[i];

                var time = TimeSpan.FromSeconds((double)i / EffectiveFps(anim.Fps));

                ProcessBoneFrameForDmeChannel(bone, frame, time, positionLogLayer, orientationLogLayer, bone == rootMotionBone);
            }

            ApplyModelDocHack(positionLogLayer);

            clip.Channels.Add(positionChannel);
            clip.Channels.Add(orientationChannel);
        }
    }

    /// <summary>
    /// Workaround for ModelDoc ignoring animation data on bone when bone doesn't have any motion
    /// </summary>
    private static void ApplyModelDocHack(DmeLogLayer<Vector3> logLayer)
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

    private static bool DoesLayerHaveMotion(DmeLogLayer<Vector3> logLayer)
    {
        if (logLayer.LayerValues.Length == 1)
        {
            return false;
        }

        var lastVal = logLayer.LayerValues[0];
        for (var i = 1; i < logLayer.LayerValues.Length; i++)
        {
            var currentVal = logLayer.LayerValues[i];

            if ((lastVal - currentVal).Length() >= 0.01f)
            {
                return true;
            }

            lastVal = currentVal;
        }

        return false;
    }
}
