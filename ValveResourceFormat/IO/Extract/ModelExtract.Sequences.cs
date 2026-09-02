using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes an animation is authored as: the sequence itself, the blends that
/// position several of them along a pose parameter, and the layers composed on top.
/// </summary>
partial class ModelExtract
{
    /// <summary>
    /// Returns the weight list a sequence applies, or <see langword="null"/> when it uses the default
    /// one every animation gets.
    /// </summary>
    static string? GetWeightListName(string sequenceName, Dictionary<string, KVObject> sequenceData, string[]? boneMaskNames)
    {
        if (boneMaskNames == null || !sequenceData.TryGetValue(sequenceName, out var seqDesc))
        {
            return null;
        }

        var index = seqDesc.GetInt32Property("m_nLocalWeightlist");

        if (index <= 0 || index >= boneMaskNames.Length)
        {
            return null;
        }

        return boneMaskNames[index];
    }

    /// <summary>
    /// Rebuilds the bone scale markup an animation was authored with, which a DMX animation carrying
    /// only position and orientation cannot hold.
    /// </summary>
    static IEnumerable<KVObject> ProcessBoneScales(Skeleton skeleton, FlexController[] flexControllers, SequenceAnimation animation)
    {
        // Quantization leaves an unresized bone a hair off one.
        const float RestScaleTolerance = 1e-3f;

        var scaledBones = animation.GetScaledBones();

        if (scaledBones.Length == 0)
        {
            yield break;
        }

        for (var frameIndex = 0; frameIndex < animation.FrameCount; frameIndex++)
        {
            var frame = new Frame(skeleton, flexControllers)
            {
                FrameIndex = frameIndex
            };

            animation.DecodeFrame(frame);

            foreach (var boneIndex in scaledBones)
            {
                var scale = frame.Bones[boneIndex].Scale;

                if (MathF.Abs(scale - 1f) <= RestScaleTolerance)
                {
                    continue;
                }

                yield return MakeNode("AnimForceBoneScale",
                    ("bone", GetExportBoneName(skeleton.Bones[boneIndex])),
                    ("scale", scale),
                    ("frame", frameIndex)
                );
            }
        }
    }

    /// <summary>
    /// Rebuilds a blend that spreads its animations over a grid of two pose parameters. Each dimension's
    /// size comes from the length of its weight list, and the grid is walked row first.
    /// </summary>
    static KVObject Process2DBlendSequence(SequenceAnimation animation, string[] localSequenceNameArray, string[] poseParamNames,
        HashSet<string> nodeNames, bool blendAnimEvents)
    {
        var fetch = animation.Fetch!.Value;
        var rows = fetch.GroupSize.Length > 0 ? (int)fetch.GroupSize[0] : 0;
        var columns = fetch.GroupSize.Length > 1 ? (int)fetch.GroupSize[1] : 0;

        string PoseParam(int dimension)
        {
            var index = fetch.LocalPose.Length > dimension ? (int)fetch.LocalPose[dimension] : -1;
            return index >= 0 && index < poseParamNames.Length ? poseParamNames[index] : string.Empty;
        }

        var rowWeights = KVObject.Array();
        var columnWeights = KVObject.Array();
        var animations = KVObject.Array();

        for (var row = 0; row < rows; row++)
        {
            rowWeights.Add(row < fetch.PoseKeyArray.Length ? fetch.PoseKeyArray[row] : 0f);

            var rowAnimations = KVObject.Array();

            for (var column = 0; column < columns; column++)
            {
                var reference = row + (rows * column);
                var localReference = reference < fetch.LocalReferenceArray.Length
                    ? (int)fetch.LocalReferenceArray[reference]
                    : -1;

                rowAnimations.Add(localReference >= 0 && localReference < localSequenceNameArray.Length
                    ? ResolveNodeName(localSequenceNameArray[localReference], nodeNames)
                    : string.Empty);
            }

            animations.Add(rowAnimations);
        }

        for (var column = 0; column < columns; column++)
        {
            var key = rows * column;
            columnWeights.Add(key < fetch.PoseKeyArray1.Length ? fetch.PoseKeyArray1[key] : 0f);
        }

        var blendNode = MakeNode("2DBlend",
            ("name", animation.Name),
            ("fade_in_time", animation.SequenceParams.FadeInTime),
            ("fade_out_time", animation.SequenceParams.FadeOutTime),
            ("looping", animation.IsLooping),
            ("delta", animation.Delta),
            ("worldSpace", animation.Worldspace),
            ("hidden", animation.Hidden),
            ("row_pose_param_name", PoseParam(0)),
            ("col_pose_param_name", PoseParam(1)),
            ("row_weight_list", rowWeights),
            ("col_weight_list", columnWeights),
            ("blend_anim_list", animations)
        );

        if (blendAnimEvents)
        {
            blendNode.Add("blend_anim_events", true);
        }

        var children = KVObject.Array();
        AddActivities(blendNode, children, animation);

        foreach (var autoLayer in animation.AutoLayers)
        {
            children.Add(ProcessAnimationAutoLayer(animation.CycleFrames, autoLayer, localSequenceNameArray, poseParamNames, nodeNames));
        }

        if (animation.Autoplay)
        {
            children.Add(MakeNode("AnimAutoLayer"));
        }

        if (children.Count > 0)
        {
            blendNode.Add("children", children);
        }

        return blendNode;
    }

    /// <summary>
    /// Rebuilds the blend node behind a sequence that plays several animations at once, positioned along
    /// a pose parameter. A sequence that only renames an animation another node declares is rebuilt
    /// through the same node, naming that node as <paramref name="aliasedAnimation"/>.
    /// </summary>
    static KVObject ProcessBlendSequence(SequenceAnimation animation, string[] localSequenceNameArray, string[] poseParamNames,
        HashSet<string> nodeNames, bool blendAnimEvents, string? aliasedAnimation = null)
    {
        var fetch = animation.Fetch!.Value;

        if (fetch.Is2D)
        {
            return Process2DBlendSequence(animation, localSequenceNameArray, poseParamNames, nodeNames, blendAnimEvents);
        }

        var poseParamIndex = fetch.LocalPose.Length > 0 ? (int)fetch.LocalPose[0] : -1;
        var poseParam = poseParamIndex >= 0 && poseParamIndex < poseParamNames.Length
            ? poseParamNames[poseParamIndex]
            : string.Empty;

        var blendList = KVObject.Array();

        for (var i = 0; i < fetch.LocalReferenceArray.Length; i++)
        {
            var reference = (int)fetch.LocalReferenceArray[i];

            if (reference < 0 || reference >= localSequenceNameArray.Length)
            {
                continue;
            }

            blendList.Add(MakeNode("AnimProxy",
                ("name", aliasedAnimation ?? ResolveNodeName(localSequenceNameArray[reference], nodeNames)),
                ("weight", i < fetch.PoseKeyArray.Length ? fetch.PoseKeyArray[i] : 0f)
            ));
        }

        var blendNode = MakeNode("1DBlend",
            ("name", animation.Name),
            ("fixed_blend", fetch.FixedBlendWeight),
            ("fixed_blend_val", fetch.FixedBlendWeightValue),
            ("fade_in_time", animation.SequenceParams.FadeInTime),
            ("fade_out_time", animation.SequenceParams.FadeOutTime),
            ("looping", animation.IsLooping),
            ("delta", animation.Delta),
            ("worldSpace", animation.Worldspace),
            ("hidden", animation.Hidden),
            ("poseParam", poseParam),
            ("blendList", blendList)
        );

        if (blendAnimEvents)
        {
            blendNode.Add("blend_anim_events", true);
        }

        var children = KVObject.Array();
        AddActivities(blendNode, children, animation);

        foreach (var autoLayer in animation.AutoLayers)
        {
            children.Add(ProcessAnimationAutoLayer(animation.CycleFrames, autoLayer, localSequenceNameArray, poseParamNames, nodeNames));
        }

        if (animation.Autoplay)
        {
            children.Add(MakeNode("AnimAutoLayer"));
        }

        if (fetch.LocalCyclePoseParameter >= 0 && fetch.LocalCyclePoseParameter < poseParamNames.Length)
        {
            children.Add(MakeNode("AnimCycleOverride",
                ("cycle_type", "Pose To Cycle"),
                ("pose_param_name", poseParamNames[fetch.LocalCyclePoseParameter])
            ));
        }

        if (children.Count > 0)
        {
            blendNode.Add("children", children);
        }

        return blendNode;
    }

    /// <summary>
    /// Matches each sequence that only plays an animation another sequence already declares to that
    /// other sequence's name, so it can be rebuilt as a one entry blend of that node. Only sequences
    /// that name a pose parameter are matched.
    /// </summary>
    static Dictionary<string, string> FindAliasedSequences(List<SequenceAnimation> sequences)
    {
        static string Bare(string name) => name.TrimStart('@');

        // A generated animation's name carries leading markers its declaring sequence does not.
        static bool DeclaresItsAnimation(SequenceAnimation animation)
            => Bare(animation.ReferencedAnimationName).Equals(Bare(animation.Name), StringComparison.OrdinalIgnoreCase);

        var declaredBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var animation in sequences)
        {
            if (animation.Fetch is { LocalReferenceArray.Length: 1 } && DeclaresItsAnimation(animation))
            {
                declaredBy.TryAdd(Bare(animation.Name), animation.Name);
            }
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var animation in sequences)
        {
            if (animation.Fetch is not { LocalReferenceArray.Length: 1, LocalPose: [>= 0, ..] }
                || DeclaresItsAnimation(animation))
            {
                continue;
            }

            if (declaredBy.TryGetValue(Bare(animation.ReferencedAnimationName), out var declared))
            {
                aliases.TryAdd(animation.Name, declared);
            }
        }

        return aliases;
    }

    /// <summary>
    /// Rebuilds the <c>FaceposerKeys</c> child node behind a sequence's <c>faceposer</c> gesture markup.
    /// The compiled markup holds a <c>type</c>/<c>tags</c> pair, with a companion key naming the tag each
    /// frame was written to. Only the gesture shape is reconstructed.
    /// </summary>
    static KVObject? ProcessFaceposerKeys(KVObject? sequenceKeys)
    {
        // The one frame written under a fixed tag name.
        const string AccentTag = "accent";

        var faceposer = sequenceKeys?.GetSubCollection("faceposer");

        if (faceposer == null || faceposer.GetStringProperty("type") != "gesture")
        {
            return null;
        }

        // Markup naming the exit tag in the singular has no FaceposerKeys class to load back into.
        if (faceposer.ContainsKey("exittag"))
        {
            return null;
        }

        // A slot with no tag alias leaves the key out entirely.
        var entryTag = faceposer.GetStringProperty("entrytag", string.Empty);
        var startLoopTag = faceposer.GetStringProperty("startloop", string.Empty);
        var endLoopTag = faceposer.GetStringProperty("endloop", string.Empty);
        var exitTag = faceposer.GetStringProperty("exittags", string.Empty);
        var tags = faceposer.GetSubCollection("tags");

        if (tags == null || entryTag.Length == 0 || startLoopTag.Length == 0 || endLoopTag.Length == 0)
        {
            return null;
        }

        var properties = new List<(string Name, KVObject Value)>
        {
            ("key_type", "Gesture"),
            ("entry", tags.GetInt32Property(entryTag, -1)),
        };

        if (tags.ContainsKey(AccentTag))
        {
            properties.Add(("accent", tags.GetInt32Property(AccentTag, -1)));
        }

        properties.Add(("start_loop", tags.GetInt32Property(startLoopTag, -1)));
        properties.Add(("end_loop", tags.GetInt32Property(endLoopTag, -1)));

        if (exitTag.Length > 0 && tags.ContainsKey(exitTag))
        {
            properties.Add(("exit", tags.GetInt32Property(exitTag, -1)));
        }

        if (faceposer.ContainsKey("thumbnail_frame"))
        {
            properties.Add(("thumbnail_frame", faceposer.GetInt32Property("thumbnail_frame")));
        }

        return MakeNode("FaceposerKeys", [.. properties]);
    }

    /// <summary>
    /// Writes an animation's primary activity onto its node, and every further one as a modifier node.
    /// </summary>
    static void AddActivities(KVObject node, KVObject children, SequenceAnimation animation)
        => AddActivities(node, children, [.. animation.Activities.Select(activity => (activity.Name, activity.Weight))]);

    static void AddActivities(KVObject node, KVObject children, (string Name, int Weight)[] activities)
    {
        if (activities.Length == 0)
        {
            return;
        }

        node.Add("activity_name", activities[0].Name);
        node.Add("activity_weight", activities[0].Weight);

        for (var i = 1; i < activities.Length; i++)
        {
            children.Add(MakeNode("ActivityModifier",
                ("activity_name", activities[i].Name),
                ("activity_weight", activities[i].Weight)
            ));
        }
    }

    /// <summary>
    /// Matches a name a sequence refers to against the nodes the document declares. The compiled name
    /// tables spell generated animations with a different case or leading marker.
    /// </summary>
    static string ResolveNodeName(string name, HashSet<string> nodeNames)
    {
        // The set is case-insensitive; a hit gives back the node's own spelling.
        if (nodeNames.TryGetValue(name, out var declared))
        {
            return declared;
        }

        var bare = name.TrimStart('@');

        foreach (var candidate in nodeNames)
        {
            if (candidate.TrimStart('@').Equals(bare, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return name;
    }

    /// <summary>
    /// Converts a declared frame count into the span a layer's cycle is measured against: the frame count
    /// minus one, or nothing for a sequence of a single frame.
    /// </summary>
    static int GetCycleFrames(int frameCount) => frameCount > 1 ? frameCount - 1 : 0;

    static KVObject ProcessAnimationAutoLayer(int cycleFrames, AnimationAutoLayer autoLayer, string[] localSequenceNameArray,
        string[] poseParamNames, HashSet<string> nodeNames)
    {
        var animName = ResolveNodeName(localSequenceNameArray[autoLayer.LocalReference], nodeNames);

        if (autoLayer.Pose == true)
        {
            var poseParam = poseParamNames[autoLayer.LocalPose];
            return MakeNode("AnimBlendLayerPoseParam", [
                ("anim_name", animName),
                ("spline", autoLayer.Spline),
                ("xfade", autoLayer.XFade),
                ("no_blend", autoLayer.NoBlend),
                ("local_space", autoLayer.Local),
                ("pose_param_name", poseParam),
                ("start_cycle", autoLayer.Start),
                ("peak_cycle", autoLayer.Peak),
                ("tail_cycle", autoLayer.Tail),
                ("end_cycle", autoLayer.End),
            ]);
        }
        else if (autoLayer.LocalPose != -1)
        {
            return MakeNode("AnimAddLayer", [
                ("anim_name", animName),
            ]);
        }
        else
        {
            return MakeNode("AnimBlendLayer", [
                ("anim_name", animName),
                ("spline", autoLayer.Spline),
                ("xfade", autoLayer.XFade),
                ("no_blend", autoLayer.NoBlend),
                ("local_space", autoLayer.Local),
                ("start_frame", (int)(autoLayer.Start * cycleFrames)),
                ("peak_frame", (int)(autoLayer.Peak * cycleFrames)),
                ("tail_frame", (int)(autoLayer.Tail * cycleFrames)),
                ("end_frame", (int)(autoLayer.End * cycleFrames)),
            ]);
        }
    }

    /// <summary>
    /// The sequence tables a model doc's animation nodes are rebuilt from, read from the model's ASEQ
    /// block. Empty throughout for a model that carries no sequences.
    /// </summary>
    private readonly record struct SequenceTables(
        KeyValuesOrNTRO? Block,
        Dictionary<string, KVObject> BySequenceName,
        IReadOnlyList<KVObject> PoseParams,
        string[]? LocalSequenceNames,
        string[]? PoseParamNames,
        string[]? BoneMaskNames)
    {
        /// <summary>
        /// Whether the tables carry the name arrays a blend or an alias is resolved against.
        /// </summary>
        public bool CanResolveReferences => LocalSequenceNames != null && PoseParamNames != null;
    }

    /// <summary>
    /// Reads the model's sequence tables out of its ASEQ block, writing nothing to the document.
    /// </summary>
    private SequenceTables ReadSequenceTables()
    {
        var block = model?.Resource?.GetBlockByType(BlockType.ASEQ) as KeyValuesOrNTRO;
        var bySequenceName = new Dictionary<string, KVObject>();

        if (block?.Data is not KVObject sequenceData)
        {
            return new SequenceTables(block, bySequenceName, [], null, null, null);
        }

        foreach (var data in sequenceData.GetArray("m_localS1SeqDescArray"))
        {
            bySequenceName.Add(data.GetStringProperty("m_sName"), data);
        }

        var poseParams = sequenceData.GetArray("m_localPoseParamArray");

        return new SequenceTables(
            block,
            bySequenceName,
            poseParams,
            sequenceData.GetArray<string>("m_localSequenceNameArray"),
            [.. poseParams.Select(x => x.GetStringProperty("m_sName"))],
            [.. sequenceData.GetArray("m_localBoneMaskArray").Select(x => x.GetStringProperty("m_sName"))]);
    }

    /// <summary>
    /// Emits the doc nodes the sequence tables carry that stand on their own: weight lists, scale sets
    /// and pose parameters. Runs before <see cref="AddAnimationNodes"/>, which references them.
    /// </summary>
    private void AddSequenceMarkupNodes(ModelDocLists lists, SequenceTables tables)
    {
        if (tables.Block is not { Data: KVObject } block)
        {
            return;
        }

        AddWeightListNodes(lists, block);
        AddScaleSetNodes(lists, block);
        AddPoseParamNodes(lists, tables.PoseParams);
    }

    private void AddAnimationNodes(ModelDocLists lists, SequenceTables tables)
    {
        if (AnimationsToExtract.Count > 0 || tables.BySequenceName.Count > 0)
        {
            var animationToFolder = new Dictionary<string, KVObject>(AnimationsToExtract.Count);
            if (tables.Block?.Data.GetSubCollection("m_keyValues") is KVObject sequenceKeyValues)
            {
                if (sequenceKeyValues.GetSubCollection("faceposer_folders") is KVObject faceposerFolders)
                {
                    foreach (var (folderName, _) in faceposerFolders)
                    {
                        var animationNames = faceposerFolders.GetArray<string>(folderName);

                        var (folderNode, children) = MakeListNode("Folder");
                        folderNode.Add("name", folderName);
                        lists.Animations.Add(folderNode);

                        foreach (var animationName in animationNames!)
                        {
                            animationToFolder.Add(animationName, children);
                        }
                    }
                }
            }

            void AddToFolderOrRoot(string name, KVObject node)
            {
                var folderOrRoot = animationToFolder.GetValueOrDefault(name, lists.Animations);
                folderOrRoot.Add(node);
            }

            var nodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var animation in AnimationsToExtract)
            {
                nodeNames.Add(animation.Anim.Name);
            }

            foreach (var name in tables.BySequenceName.Keys)
            {
                nodeNames.Add(name);
            }

            foreach (var (name, aseq) in tables.BySequenceName)
            {
                // A sequence that plays no animation directly is either the bind pose, flagged
                // "bind_pose", or an EmptyAnim, carrying an explicit frame count and rate instead.
                var playsNothing = aseq.GetSubCollection("m_fetch").GetIntegerArray("m_localReferenceArray").Length == 0;
                var sequenceKeys = aseq.GetSubCollection("m_SequenceKeys");

                if (playsNothing || sequenceKeys?.GetBooleanProperty("bind_pose") == true)
                {
                    var emptyAnimKeys = sequenceKeys?.GetSubCollection("keyvalues");
                    var isEmptyAnim = emptyAnimKeys != null && emptyAnimKeys.TryGetValue("numframes", out _);

                    var transition = aseq.GetSubCollection("m_transition");
                    var bindPoseFlags = aseq.GetSubCollection("m_flags");
                    var bindPose = MakeNode(isEmptyAnim ? "EmptyAnim" : "AnimBindPose",
                        ("name", name),
                        ("fade_in_time", transition.GetFloatProperty("m_flFadeInTime")),
                        ("fade_out_time", transition.GetFloatProperty("m_flFadeOutTime")),
                        ("looping", bindPoseFlags.GetBooleanProperty("m_bLooping")),
                        ("delta", bindPoseFlags.GetBooleanProperty("m_bLegacyDelta")),
                        ("worldSpace", bindPoseFlags.GetBooleanProperty("m_bLegacyWorldspace")),
                        ("hidden", bindPoseFlags.GetBooleanProperty("m_bHidden"))
                    );

                    var frameCount = 0;

                    if (isEmptyAnim)
                    {
                        frameCount = emptyAnimKeys!.GetInt32Property("numframes");
                        bindPose.Add("frame_count", frameCount);
                        bindPose.Add("frame_rate", emptyAnimKeys!.GetFloatProperty("fps"));
                    }

                    var bindPoseWeightList = GetWeightListName(name, tables.BySequenceName, tables.BoneMaskNames);

                    if (bindPoseWeightList != null)
                    {
                        bindPose.Add("weight_list_name", bindPoseWeightList);
                    }

                    var bindPoseChildren = KVObject.Array();

                    AddActivities(bindPose, bindPoseChildren, [.. aseq.GetArray("m_activityArray")
                        .Select(activity => (activity.GetStringProperty("m_name"), activity.GetInt32Property("m_nWeight")))]);

                    // A bind pose declares no length, so its layers keep only their target and blend flags.
                    if (tables.LocalSequenceNames != null)
                    {
                        foreach (var autoLayerKV in aseq.GetArray("m_autoLayerArray"))
                        {
                            var autoLayer = new AnimationAutoLayer(autoLayerKV);
                            bindPoseChildren.Add(ProcessAnimationAutoLayer(GetCycleFrames(frameCount), autoLayer, tables.LocalSequenceNames, tables.PoseParamNames ?? [], nodeNames));
                        }
                    }

                    if (bindPoseFlags.GetBooleanProperty("m_bAutoplay"))
                    {
                        bindPoseChildren.Add(MakeNode("AnimAutoLayer"));
                    }

                    var bindPoseCyclePose = aseq.GetSubCollection("m_fetch").GetInt32Property("m_nLocalCyclePoseParameter");

                    if (tables.PoseParamNames != null && bindPoseCyclePose >= 0 && bindPoseCyclePose < tables.PoseParamNames.Length)
                    {
                        bindPoseChildren.Add(MakeNode("AnimCycleOverride", [
                            ("cycle_type", "Pose To Cycle"),
                            ("pose_param_name", tables.PoseParamNames[bindPoseCyclePose]),
                        ]));
                    }

                    if (bindPoseFlags.GetBooleanProperty("m_bLegacyRealtime"))
                    {
                        bindPoseChildren.Add(MakeNode("AnimCycleOverride", [
                            ("cycle_type", "Auto Cycle"),
                            ("pose_param_name", ""),
                        ]));
                    }

                    if (ProcessFaceposerKeys(sequenceKeys) is KVObject bindPoseFaceposerKeys)
                    {
                        bindPoseChildren.Add(bindPoseFaceposerKeys);
                    }

                    if (bindPoseChildren.Count > 0)
                    {
                        bindPose.Add("children", bindPoseChildren);
                    }

                    AddToFolderOrRoot(name, bindPose);
                }
            }

            var sequences = AnimationsToExtract.Where(x => HasOwnAnimFileNode(x.Anim));
            var aliases = AliasedSequences;

            foreach (var animation in sequences)
            {
                var isAlias = aliases.TryGetValue(animation.Anim.Name, out var aliasedAnimation);

                if ((animation.Anim.IsBlend || isAlias)
                    && tables is { LocalSequenceNames: { } blendSequenceNames, PoseParamNames: { } blendPoseParamNames })
                {
                    var blendAnimEvents = tables.BySequenceName.TryGetValue(animation.Anim.Name, out var blendSequenceData)
                        && blendSequenceData.GetSubCollection("m_SequenceKeys")?.GetBooleanProperty("blend_anim_events") == true;

                    var blendNode = ProcessBlendSequence(animation.Anim, blendSequenceNames, blendPoseParamNames, nodeNames, blendAnimEvents, aliasedAnimation);
                    var blendWeightList = GetWeightListName(animation.Anim.Name, tables.BySequenceName, tables.BoneMaskNames);

                    if (blendWeightList != null)
                    {
                        blendNode.Add("weight_list_name", blendWeightList);
                    }

                    AddToFolderOrRoot(animation.Anim.Name, blendNode);
                    continue;
                }

                var animationFile = MakeNode(
                    "AnimFile",
                    ("name", animation.Anim.Name),
                    ("source_filename", animation.FileName),
                    ("fade_in_time", animation.Anim.SequenceParams.FadeInTime),
                    ("fade_out_time", animation.Anim.SequenceParams.FadeOutTime),
                    ("looping", animation.Anim.IsLooping),
                    ("delta", animation.Anim.Delta),
                    ("worldSpace", animation.Anim.Worldspace),
                    ("hidden", animation.Anim.Hidden)
                );

                var childrenKV = KVObject.Array();

                AddActivities(animationFile, childrenKV, animation.Anim);

                var weightList = GetWeightListName(animation.Anim.Name, tables.BySequenceName, tables.BoneMaskNames);

                if (weightList != null)
                {
                    animationFile.Add("weight_list_name", weightList);
                }

                foreach (var localHierarchy in animation.Anim.LocalHierarchy)
                {
                    childrenKV.Add(MakeNode("LocalHierarchy",
                        ("bone_name", localHierarchy.Bone),
                        ("new_parent_bone_name", localHierarchy.NewParent),
                        ("start_frame", localHierarchy.StartFrame),
                        ("peak_frame", localHierarchy.PeakFrame),
                        ("tail_frame", localHierarchy.TailFrame),
                        ("end_frame", localHierarchy.EndFrame)
                    ));
                }

                if (model != null)
                {
                    foreach (var boneScale in ProcessBoneScales(model.Skeleton, model.FlexControllers, animation.Anim))
                    {
                        childrenKV.Add(boneScale);
                    }
                }

                if (animation.Anim.HasMovementData())
                {
                    var flags = animation.Anim.Movements[0].MotionFlags;
                    var extractMotion = MakeNode("ExtractMotion",
                        ("extract_tx", flags.HasFlag(ModelAnimationMotionFlags.TX)),
                        ("extract_ty", flags.HasFlag(ModelAnimationMotionFlags.TY)),
                        // never extract vertical. on recompile it makes the compiler counter-bake the root
                        // and float the whole model up. the engine doesn't apply vertical root motion.
                        ("extract_tz", false),
                        ("extract_rz", flags.HasFlag(ModelAnimationMotionFlags.RZ)),
                        ("linear", flags.HasFlag(ModelAnimationMotionFlags.Linear)),
                        ("quadratic", false),
                        ("motion_type", "uniform")
                    );

                    childrenKV.Add(extractMotion);
                }
                foreach (var animEvent in animation.Anim.Events)
                {
                    var animEventNode = MakeNode("AnimEvent",
                        ("event_class", animEvent.Name),
                        ("event_frame", animEvent.Frame)
                    );

                    // An event's duration is the span between its frame and its end frame.
                    if (animEvent.EndFrame != -1)
                    {
                        animEventNode.Add("event_end_frame", animEvent.EndFrame);
                    }

                    if (animEvent.EventData != null)
                    {
                        animEventNode.Add("event_keys", animEvent.EventData);
                    }
                    childrenKV.Add(animEventNode);
                }

                if (tables is { LocalSequenceNames: { } layerSequenceNames, PoseParamNames: { } layerPoseParamNames })
                {
                    foreach (var autoLayer in animation.Anim.AutoLayers)
                    {
                        var layerNode = ProcessAnimationAutoLayer(animation.Anim.CycleFrames, autoLayer, layerSequenceNames, layerPoseParamNames, nodeNames);
                        childrenKV.Add(layerNode);
                    }
                }

                if (animation.Anim.Autoplay)
                {
                    var autoLayer = MakeNode("AnimAutoLayer");
                    childrenKV.Add(autoLayer);
                }

                if (tables.PoseParamNames != null && animation.Anim.Fetch != null && animation.Anim.Fetch.Value.LocalCyclePoseParameter != -1)
                {
                    var poseParamIndex = animation.Anim.Fetch.Value.LocalCyclePoseParameter;
                    var poseParam = tables.PoseParamNames[poseParamIndex];

                    var autoLayer = MakeNode("AnimCycleOverride", [
                        ("cycle_type", "Pose To Cycle"),
                        ("pose_param_name", poseParam),
                    ]);
                    childrenKV.Add(autoLayer);
                }

                if (animation.Anim.Realtime)
                {
                    var autoLayer = MakeNode("AnimCycleOverride", [
                        ("cycle_type", "Auto Cycle"),
                        ("pose_param_name", ""),
                    ]);
                    childrenKV.Add(autoLayer);
                }

                if (tables.BySequenceName.TryGetValue(animation.Anim.Name, out var animSequenceData))
                {
                    var sequenceKeys = animSequenceData.GetSubCollection("m_SequenceKeys");
                    if (sequenceKeys != null)
                    {
                        // other keys seen:
                        // bind_pose = true

                        if (sequenceKeys.GetSubCollection("AnimGameplayTiming") is KVObject animGameplayTiming)
                        {
                            childrenKV.Add(MakeNode("AnimGameplayTiming", animGameplayTiming));
                        }

                        if (ProcessFaceposerKeys(sequenceKeys) is KVObject faceposerKeys)
                        {
                            childrenKV.Add(faceposerKeys);
                        }
                    }
                }

                if (childrenKV.Count > 0)
                {
                    animationFile.Add("children", childrenKV);
                }

                AddToFolderOrRoot(animation.Anim.Name, animationFile);
            }
        }
    }
}
