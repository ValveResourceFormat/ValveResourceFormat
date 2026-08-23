using System.IO;
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
        return (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + Path.GetFileNameWithoutExtension(fileName) // so models in the same directory do not override each other's anims
            + "_"
            + animationName
            + ".dmx")
            .Replace('\\', '/');
    }

    /// <summary>
    /// Produces a skeleton DMX file. <paramref name="nmLowLodBoneCount"/> is the skeleton's
    /// m_numBonesToSampleAtLowLOD; when non-negative, DAG children are ordered so that
    /// CompileNmSkeleton emits the bones in their original compiled order.
    /// </summary>
    public static byte[] ToDmxSkeleton(Skeleton skeleton, bool nmSkelAxisFixup = false, int nmLowLodBoneCount = -1)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);

        var dmeSkeleton = BuildDmeDagSkeleton(skeleton, out var transforms, nmSkelAxisFixup, nmLowLodBoneCount);

        using var stream = new MemoryStream();

        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeSkeleton,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["app"] = "sfm", // maya
                ["source"] = $"Generated with {StringToken.VRF_GENERATOR}",
            }
        };

        dmx.Save(stream, "keyvalues2", 4);
        return stream.ToArray();
    }

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

    private static readonly Quaternion NmSkelRotationFixup = new(-0.5f, -0.5f, -0.5f, 0.5f);
    private static readonly Quaternion NmSkelRotationFixupInverse = Quaternion.Inverse(NmSkelRotationFixup);

    /// <summary>
    /// NM skeletons compile the bones under root_motion in a permuted axis frame. This re-frames one of
    /// them, in root_motion's space, which is why the rotation is multiplied on the left.
    /// </summary>
    private static (Vector3 Position, Quaternion Rotation) NmAxisFixupChild(Vector3 position, Quaternion rotation)
        => (Vector3.Transform(position, NmSkelRotationFixup), NmSkelRotationFixup * rotation);

    /// <summary>
    /// The other half of <see cref="NmAxisFixupChild"/>. root_motion takes the inverse, so what its children
    /// gained cancels against it and the subtree below them does not move.
    /// </summary>
    /// <remarks>
    /// Both halves have to be applied to the bind pose and to every frame alike, since a clip writes a
    /// channel for each of these bones and the frame values override what the bind pose declared.
    /// </remarks>
    private static Quaternion NmAxisFixupRootMotion(Quaternion rotation)
        => rotation * NmSkelRotationFixupInverse;

    /// <summary>Emits cloth bones with the '_' prefix the compiler sanitizes '$' to, so round-trips don't duplicate them.</summary>
    internal static string GetExportBoneName(Bone bone)
        => bone.IsProceduralCloth && bone.Name.StartsWith('$')
            ? $"_{bone.Name[1..]}"
            : bone.Name;

    private static DmeModel BuildDmeDagSkeleton(Skeleton skeleton, out DmeTransform[] transforms, bool nmSkelAxisFixup = false, int nmLowLodBoneCount = -1)
    {
        var dmeSkeleton = new DmeModel();

        transforms = AppendDmeSkeletonJoints(dmeSkeleton, skeleton, nmLowLodBoneCount);

        var rootMotionBone = skeleton["root_motion"];

        if (nmSkelAxisFixup && rootMotionBone != null)
        {
            // dmeSkeleton.AxisSystem.UpAxis = 2;
            // dmeSkeleton.AxisSystem.ForwardParity = -1;
            // dmeSkeleton.AxisSystem.CoordSys = 2;

            transforms[rootMotionBone.Index].Orientation = NmAxisFixupRootMotion(transforms[rootMotionBone.Index].Orientation);

            foreach (var root in rootMotionBone.Children)
            {
                (transforms[root.Index].Position, transforms[root.Index].Orientation)
                    = NmAxisFixupChild(transforms[root.Index].Position, transforms[root.Index].Orientation);
            }
        }

        return dmeSkeleton;
    }

    /// <summary>
    /// Adds one skeleton's joints to a DmeModel, its roots as children of the model, and returns the
    /// joint transforms indexed by bone index. When <paramref name="nmLowLodBoneCount"/> is
    /// non-negative, DAG siblings are ordered to reproduce the skeleton's compiled NM bone order;
    /// otherwise they are appended in bone index order.
    /// </summary>
    private static DmeTransform[] AppendDmeSkeletonJoints(DmeModel dmeSkeleton, Skeleton skeleton, int nmLowLodBoneCount = -1)
    {
        var childOrder = NmCompilerChildOrder(skeleton, nmLowLodBoneCount);
        var transforms = new DmeTransform[skeleton.Bones.Length];
        var boneDags = new DmeJoint[skeleton.Bones.Length];

        foreach (var bone in skeleton.Bones)
        {
            var boneName = GetExportBoneName(bone);
            var dag = new DmeJoint
            {
                Name = boneName
            };

            dag.Transform.Name = boneName;
            dag.Transform.Position = bone.Position;
            dag.Transform.Orientation = bone.Angle;

            boneDags[bone.Index] = dag;
            transforms[bone.Index] = dag.Transform;

            dmeSkeleton.JointList.Add(dag);
        }

        foreach (var bone in skeleton.Bones)
        {
            foreach (var child in SortedSiblings(bone.Children, childOrder))
            {
                boneDags[bone.Index].Children.Add(boneDags[child.Index]);
            }
        }

        foreach (var root in SortedSiblings(skeleton.Roots, childOrder))
        {
            dmeSkeleton.Children.Add(boneDags[root.Index]);
        }

        return transforms;
    }

    private static IReadOnlyList<Bone> SortedSiblings(IReadOnlyList<Bone> siblings, Func<IReadOnlyList<Bone>, IReadOnlyList<Bone>>? childOrder)
    {
        if (childOrder == null || siblings.Count < 2)
        {
            return siblings;
        }

        return childOrder(siblings);
    }

    /// <summary>
    /// CompileNmSkeleton emits bones as a hierarchy walk filtered to the first
    /// m_numBonesToSampleAtLowLOD bones, then the same walk filtered to the rest. This builds the
    /// sibling order whose walk reproduces the skeleton's compiled bone order, keeping recompiled
    /// skeletons index-compatible with existing clips: siblings whose subtrees contain low-LOD
    /// bones keep their relative compiled order, and each pure high-LOD sibling is placed where
    /// its subtree's first high-LOD bone falls between theirs.
    /// </summary>
    private static Func<IReadOnlyList<Bone>, IReadOnlyList<Bone>>? NmCompilerChildOrder(Skeleton skeleton, int nmLowLodBoneCount)
    {
        if (nmLowLodBoneCount < 0)
        {
            return null;
        }

        var boneCount = skeleton.Bones.Length;
        var minLow = new int[boneCount];
        var minHigh = new int[boneCount];
        Array.Fill(minLow, int.MaxValue);
        Array.Fill(minHigh, int.MaxValue);

        for (var i = boneCount - 1; i >= 0; i--)
        {
            if (i < nmLowLodBoneCount)
            {
                minLow[i] = Math.Min(minLow[i], i);
            }
            else
            {
                minHigh[i] = Math.Min(minHigh[i], i);
            }

            var parent = skeleton.Bones[i].Parent;
            if (parent != null)
            {
                minLow[parent.Index] = Math.Min(minLow[parent.Index], minLow[i]);
                minHigh[parent.Index] = Math.Min(minHigh[parent.Index], minHigh[i]);
            }
        }

        return siblings =>
        {
            var lowContaining = new List<Bone>();
            var highOnly = new List<Bone>();

            foreach (var sibling in siblings)
            {
                (minLow[sibling.Index] != int.MaxValue ? lowContaining : highOnly).Add(sibling);
            }

            lowContaining.Sort((a, b) => minLow[a.Index].CompareTo(minLow[b.Index]));
            highOnly.Sort((a, b) => minHigh[a.Index].CompareTo(minHigh[b.Index]));

            var merged = new List<Bone>(siblings.Count);
            var next = 0;

            foreach (var sibling in lowContaining)
            {
                if (minHigh[sibling.Index] != int.MaxValue)
                {
                    while (next < highOnly.Count && minHigh[highOnly[next].Index] < minHigh[sibling.Index])
                    {
                        merged.Add(highOnly[next++]);
                    }
                }

                merged.Add(sibling);
            }

            merged.AddRange(highOnly.GetRange(next, highOnly.Count - next));
            return merged;
        };
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
            var time = i / MathF.Max(1f, anim.Fps);
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
                var time = TimeSpan.FromSeconds((double)i / MathF.Max(1f, anim.Fps));
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

                var time = TimeSpan.FromSeconds((double)i / MathF.Max(1f, anim.Fps));

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
