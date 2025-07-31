using System.IO;
using Datamodel;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    public List<(Animation Anim, string FileName)> AnimationsToExtract { get; } = [];

    private void EnqueueAnimations()
    {
        if (model != null)
        {
            foreach (var anim in model.GetEmbeddedAnimations())
            {
                AnimationsToExtract.Add((anim, GetDmxFileName_ForAnimation(anim.Name)));
            }
        }
    }

    string GetDmxFileName_ForAnimation(string animationName)
    {
        var fileName = GetModelName();
        return (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + animationName
            + ".dmx")
            .Replace('\\', '/');
    }

    public static byte[] ToDmxAnim(Model model, Animation anim)
        => ToDmxAnim(model.Skeleton, model.FlexControllers, anim);

    public static byte[] ToDmxAnim(Skeleton skeleton, FlexController[] flexControllers, Animation anim)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);

        var dmeSkeleton = BuildDmeDagSkeleton(skeleton, out var transforms);

        var animationList = new DmeAnimationList();
        var clip = new DmeChannelsClip
        {
            FrameRate = anim.Fps
        };

        if (anim.FrameCount > 0)
        {
            clip.TimeFrame.Duration = TimeSpan.FromSeconds((double)(anim.FrameCount - 1) / MathF.Max(1f, anim.Fps));

            var frames = new Frame[anim.FrameCount];
            for (var i = 0; i < anim.FrameCount; i++)
            {
                var frame = new Frame(skeleton, flexControllers)
                {
                    FrameIndex = i
                };
                anim.DecodeFrame(frame);
                frames[i] = frame;
            }

            ProcessRootMotionChannel(anim, dmeSkeleton, clip);
            ProcessBoneChannels(skeleton, anim, transforms, clip, frames);
            ProcessFlexChannels(flexControllers, anim, clip, frames);
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

        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    private static DmeModel BuildDmeDagSkeleton(Skeleton skeleton, out DmeTransform[] transforms)
    {
        var dmeSkeleton = new DmeModel();
        var children = new ElementArray();

        transforms = new DmeTransform[skeleton.Bones.Length];
        var boneDags = new DmeJoint[skeleton.Bones.Length];

        foreach (var bone in skeleton.Bones)
        {
            var dag = new DmeJoint
            {
                Name = bone.Name
            };

            dag.Transform.Name = bone.Name;
            dag.Transform.Position = bone.Position;
            dag.Transform.Orientation = bone.Angle;

            boneDags[bone.Index] = dag;
            transforms[bone.Index] = dag.Transform;
        }

        foreach (var bone in skeleton.Bones)
        {
            var boneDag = boneDags[bone.Index];
            if (bone.Parent != null)
            {
                var parentDag = boneDags[bone.Parent.Index];
                parentDag.Children.Add(boneDag);
            }
            else
            {
                dmeSkeleton.Children.Add(boneDag);
            }
        }

        return dmeSkeleton;
    }

    private static DmeChannel BuildDmeChannel<T>(string name, Element toElement, string toAttribute, out DmeLog<T> log)
    {
        var channel = new DmeChannel
        {
            Name = name,
            ToElement = toElement,
            ToAttribute = toAttribute,
            Mode = 3
        };

        log = [];
        var logLayer = new DmeLogLayer<T>();

        channel.Log = log;
        log.AddLayer(logLayer);

        return channel;
    }

    private static void ProcessBoneFrameForDmeChannel(Bone bone, Frame frame, TimeSpan time, DmeLogLayer<Vector3> positionLayer, DmeLogLayer<Quaternion> orientationLayer)
    {
        var frameBone = frame.Bones[bone.Index];

        positionLayer.Times.Add(time);
        positionLayer.LayerValues[frame.FrameIndex] = frameBone.Position;

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
            var time = i / MathF.Max(1f, anim.Fps);
            var timespan = TimeSpan.FromSeconds(time);

            var movement = anim.GetMovementOffsetData(time);

            rootPositionLayer.LayerValues[i] = movement.Position;
            rootPositionLayer.Times.Add(timespan);

            var degrees = movement.Angle * 0.0174532925f; //Deg to rad
            rootOrientationLayer.LayerValues[i] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees);
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
                var time = TimeSpan.FromSeconds((double)i / MathF.Max(1f, anim.Fps));
                ProcessFlexFrameForDmeChannel(flexId, frame, time, flexLogLayer);
            }
            clip.Channels.Add(flexChannel);
        }
    }

    private static void ProcessBoneChannels(Skeleton skeleton, Animation anim, DmeTransform[] transforms, DmeChannelsClip clip, Frame[] frames)
    {
        foreach (var bone in skeleton.Bones)
        {
            var transform = transforms[bone.Index];

            var positionChannel = BuildDmeChannel<Vector3>($"{bone.Name}_p", transform, "position", out var positionLog);
            var orientationChannel = BuildDmeChannel<Quaternion>($"{bone.Name}_o", transform, "orientation", out var orientationLog);

            var positionLogLayer = positionLog.GetLayer(0);
            var orientationLogLayer = orientationLog.GetLayer(0);

            positionLogLayer.LayerValues = new Vector3[anim.FrameCount];
            orientationLogLayer.LayerValues = new Quaternion[anim.FrameCount];

            for (var i = 0; i < anim.FrameCount; i++)
            {
                var frame = frames[i];

                var time = TimeSpan.FromSeconds((double)i / MathF.Max(1f, anim.Fps));

                ProcessBoneFrameForDmeChannel(bone, frame, time, positionLogLayer, orientationLogLayer);
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
        logLayer.Times = newTimes;
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
