using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    #region Bone Constraints
    static string? RemapBoneConstraintClassname(string className)
    {
        return className switch
        {
            "CTiltTwistConstraint" => "AnimConstraintTiltTwist",
            "CTwistConstraint" => "AnimConstraintTwist",
            "CAimConstraint" => "AnimConstraintAim",
            "COrientConstraint" => "AnimConstraintOrient",
            "CPointConstraint" => "AnimConstraintPoint",
            "CParentConstraint" => "AnimConstraintParent",
            "CMorphConstraint" => "AnimConstraintMorph",
            "CBoneConstraintPoseSpaceBone" => "AnimConstraintPoseSpaceBone",
            "CBoneConstraintPoseSpaceMorph" => "AnimConstraintPoseSpaceMorph",
            "CBoneConstraintDotToMorph" => "AnimConstraintDotToMorph",
            _ => null
        };
    }

    static void AddBoneConstraintProperty<T>(KVObject sourceObject, KVObject targetObject, string sourceName, string targetName)
    {
        if (sourceObject.ContainsKey(sourceName))
        {
            if (typeof(T) == typeof(Quaternion))
            {
                var value = sourceObject.GetFloatArray(sourceName);
                var rot = new Quaternion(value[0], value[1], value[2], value[3]);
                var angles = ToEulerAngles(rot);
                targetObject.Add(targetName, ToKVArray(angles));
            }
            else if (typeof(T) == typeof(Vector3))
            {
                var value = sourceObject.GetFloatArray(sourceName);
                var pos = new Vector3(value[0], value[1], value[2]);
                targetObject.Add(targetName, ToKVArray(pos));
            }
            else
            {
                targetObject.Add(targetName, sourceObject[sourceName]);
            }
        }
    }

    static KVObject? ProcessBoneConstraintTarget(KVObject target)
    {
        var isAttachment = target.GetProperty<bool>("m_bIsAttachment");
        var targetHash = target.GetUInt32Property("m_nBoneHash");
        if (!StringToken.InvertedTable.TryGetValue(targetHash, out var targetName))
        {
#if DEBUG
            Console.WriteLine($"Couldn't find name of {(isAttachment ? "attachment" : "bone")} for bone constraint: {targetHash}");
#endif
            return null;
        }

        KVObject node;
        if (isAttachment)
        {
            node = MakeNode("AnimConstraintAttachmentInput", ("parent_attachment", targetName));
        }
        else
        {
            node = MakeNode("AnimConstraintBoneInput", ("parent_bone", targetName));
        }

        AddBoneConstraintProperty<double>(target, node, "m_flWeight", "weight");
        AddBoneConstraintProperty<Vector3>(target, node, "m_vOffset", "relative_origin");
        AddBoneConstraintProperty<Quaternion>(target, node, "m_qOffset", "relative_angles");
        return node;
    }

    static KVObject? ProcessBoneConstraintSlave(KVObject slave)
    {
        var boneHash = slave.GetUInt32Property("m_nBoneHash");
        if (!StringToken.InvertedTable.TryGetValue(boneHash, out var boneName))
        {
#if DEBUG
            Console.WriteLine($"Couldn't find name of bone for bone constraint: {boneHash}");
#endif
            return null;
        }

        var node = MakeNode("AnimConstraintSlave", ("parent_bone", boneName));
        AddBoneConstraintProperty<double>(slave, node, "m_flWeight", "weight");
        AddBoneConstraintProperty<Vector3>(slave, node, "m_vBasePosition", "relative_origin");
        AddBoneConstraintProperty<Quaternion>(slave, node, "m_qBaseOrientation", "relative_angles");
        return node;
    }

    static void ProcessBoneConstraintChildren(KVObject boneConstraint, KVObject node)
    {
        var targets = boneConstraint.GetArray("m_targets")
                                    .Select(p => ProcessBoneConstraintTarget(p))
                                    .OfType<KVObject>();

        IEnumerable<KVObject> children;
        if (node.GetStringProperty("_class") == "AnimConstraintParent")
        {
            //Parent constraints only have a single slave and it's not a child node in the .vmdl
            children = targets;

            var constrainedBoneData = boneConstraint.GetArray("m_slaves")[0];
            AddBoneConstraintProperty<double>(constrainedBoneData, node, "m_flWeight", "weight");
            AddBoneConstraintProperty<Vector3>(constrainedBoneData, node, "m_vBasePosition", "translation_offset");

            //Order of angles is different for some reason
            var rotArray = constrainedBoneData.GetFloatArray("m_qBaseOrientation");
            var rot = new Quaternion(rotArray[0], rotArray[1], rotArray[2], rotArray[3]);
            var angles = ToEulerAngles(rot);
            angles = new Vector3(angles.Z, angles.X, angles.Y);
            node.Add("rotation_offset_xyz", ToKVArray(angles));
        }
        else
        {
            var slaves = boneConstraint.GetArray("m_slaves")
                                        .Select(p => ProcessBoneConstraintSlave(p))
                                        .OfType<KVObject>();

            children = slaves.Concat(targets);
        }

        var childrenKV = KVObject.Array();
        foreach (var child in children)
        {
            childrenKV.Add(child);
        }
        node.Add("children", childrenKV);
    }

    static KVObject? ProcessBoneConstraint(KVObject? boneConstraint)
    {
        if (boneConstraint == null) //ModelDoc will compile constraints as null if it considers them invalid
        {
            return null;
        }

        var className = boneConstraint.GetStringProperty("_class");
        var targetClassName = RemapBoneConstraintClassname(className);
        if (targetClassName == null)
        {
#if DEBUG
            Console.WriteLine($"Skipping unknown bone constraint type: {className}");
#endif
            return null;
        }

        var node = MakeNode(targetClassName);

        // These constraints are stored the same way in the .vmdl and the compiled model
        if (targetClassName is "AnimConstraintPoseSpaceBone"
                            or "AnimConstraintPoseSpaceMorph"
                            or "AnimConstraintDotToMorph")
        {

            return MakeNode(targetClassName, boneConstraint);
        }

        ProcessBoneConstraintChildren(boneConstraint, node);

        AddBoneConstraintProperty<long>(boneConstraint, node, "m_nTargetAxis", "input_axis");
        AddBoneConstraintProperty<long>(boneConstraint, node, "m_nSlaveAxis", "slave_axis");
        AddBoneConstraintProperty<Quaternion>(boneConstraint, node, "m_qAimOffset", "aim_offset");
        AddBoneConstraintProperty<Vector3>(boneConstraint, node, "m_vUpVector", "up_vector");
        AddBoneConstraintProperty<long>(boneConstraint, node, "m_nUpType", "up_type");
        AddBoneConstraintProperty<Quaternion>(boneConstraint, node, "m_qParentBindRotation", "parent_bind_rotation");
        AddBoneConstraintProperty<Quaternion>(boneConstraint, node, "m_qChildBindRotation", "child_bind_rotation");
        AddBoneConstraintProperty<bool>(boneConstraint, node, "m_bInverse", "inverse");
        AddBoneConstraintProperty<string>(boneConstraint, node, "m_sTargetMorph", "target_morph_control");
        AddBoneConstraintProperty<long>(boneConstraint, node, "m_nSlaveChannel", "slave_channel");
        AddBoneConstraintProperty<double>(boneConstraint, node, "m_flMin", "min");
        AddBoneConstraintProperty<double>(boneConstraint, node, "m_flMax", "max");

        return node;
    }

    KVObject ExtractBoneConstraints(KVObject[] boneConstraintsList)
    {
        Debug.Assert(model is not null);

        var stringTokenKeys = model.Skeleton.Bones.Select(b => b.Name);
        if (RenderMeshesToExtract.Count > 0)
        {
            var mesh = RenderMeshesToExtract.First().Mesh;
            stringTokenKeys = stringTokenKeys.Concat(mesh.Attachments.Keys);
        }

        StringToken.Store(stringTokenKeys);

        var childrenKV = KVObject.Array();

        foreach (var boneConstraint in boneConstraintsList)
        {
            var constraint = ProcessBoneConstraint(boneConstraint);
            if (constraint != null)
            {
                childrenKV.Add(constraint);
            }
        }

        var constraintListNode = MakeNode("AnimConstraintList",
            ("children", childrenKV)
        );

        return constraintListNode;
    }
    #endregion

    internal static Vector3 ToEulerAngles(Quaternion q)
    {
        Vector3 angles = new();

        // pitch / x
        var sinp = 2 * (q.W * q.Y - q.Z * q.X);
        if (Math.Abs(sinp) >= 1)
        {
            angles.X = MathF.CopySign(MathF.PI / 2, sinp);
        }
        else
        {
            angles.X = MathF.Asin(sinp);
        }

        // yaw / y
        var siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        var cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        angles.Y = MathF.Atan2(siny_cosp, cosy_cosp);

        // roll / z
        var sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        var cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        angles.Z = MathF.Atan2(sinr_cosp, cosr_cosp);

        return Vector3.RadiansToDegrees(angles);
    }

    static void AddBonesRecursive(IEnumerable<Bone> bones, KVObject parent)
    {
        foreach (var bone in bones)
        {
            var boneDefinitionNode = MakeNode(
                "Bone",
                ("name", bone.Name),
                ("origin", ToKVArray(bone.Position)),
                ("angles", ToKVArray(ToEulerAngles(bone.Angle))),
                ("do_not_discard", true)
            );

            parent.Add(boneDefinitionNode);

            if (bone.Children.Count > 0)
            {
                var childBones = KVObject.Array();
                boneDefinitionNode.Add("children", childBones);
                AddBonesRecursive(bone.Children, childBones);
            }
        }
    }

    static KVObject ProcessAnimationAutoLayer(Animation animation, AnimationAutoLayer autoLayer, string[] localSequenceNameArray, string[] poseParamNames)
    {
        var animName = localSequenceNameArray[autoLayer.LocalReference];

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
                ("start_frame", (int)(autoLayer.Start * animation.FrameCount)),
                ("peak_frame", (int)(autoLayer.Peak * animation.FrameCount)),
                ("tail_frame", (int)(autoLayer.Tail * animation.FrameCount)),
                ("end_frame", (int)(autoLayer.End * animation.FrameCount)),
            ]);
        }
    }

    /// <summary>
    /// Converts the model to Valve model format as a string.
    /// </summary>
    public string ToValveModel()
    {
        Debug.Assert(model is not null, "model should not be null when converting to ValveModel");

        var kv = KVObject.Collection();

        var root = MakeListNode("RootNode");
        kv.Add("rootNode", root.Node);

        Lazy<KVObject> MakeLazyList(string className)
        {
            return new Lazy<KVObject>(() =>
            {
                var list = MakeListNode(className);
                root.Children.Add(list.Node);

                return list.Children;
            });
        }

        var materialGroupList = MakeLazyList("MaterialGroupList");
        var renderMeshList = MakeLazyList("RenderMeshList");
        var bodyGroupList = MakeLazyList("BodyGroupList");
        var animationList = MakeLazyList("AnimationList");
        var physicsShapeList = MakeLazyList("PhysicsShapeList");
        var attachmentList = MakeLazyList("AttachmentList");
        var skeleton = MakeLazyList("Skeleton");
        var modelModifierList = MakeLazyList("ModelModifierList");
        var weightLists = MakeLazyList("WeightListList");
        var hitboxSetList = MakeLazyList("HitboxSetList");
        var poseParamList = MakeLazyList("PoseParamList");

        var nmskelList = MakeLazyList("NmSkeletonList");
        var animGraph2List = MakeLazyList("AnimGraph2List");

        var boneMarkupList = MakeListNode("BoneMarkupList");
        root.Children.Add(boneMarkupList.Node);
        boneMarkupList.Node.Add("bone_cull_type", "None");

        if (RenderMeshesToExtract.Count != 0)
        {
            foreach (var renderMesh in RenderMeshesToExtract)
            {
                var renderMeshFile = MakeNode(
                    "RenderMeshFile",
                    ("name", renderMesh.Name),
                    ("filename", renderMesh.FileName)
                );

                if (renderMesh.ImportFilter != default)
                {
                    var importFilter = KVObject.Collection();
                    {
                        importFilter.Add("exclude_by_default", renderMesh.ImportFilter.ExcludeByDefault);
                        importFilter.Add("exception_list", MakeArray([.. renderMesh.ImportFilter.Filter.Select(s => (KVObject)s)]));
                    }

                    renderMeshFile.Add("import_filter", importFilter);
                }

                renderMeshList.Value.Add(renderMeshFile);
            }

            {
                // Mesh/Body Groups
                var meshGroups = model.Data.GetArray<string>("m_meshGroups");
                var meshGroupMasks = model.Data.GetUnsignedIntegerArray("m_refMeshGroupMasks");
                var hideInTools = Array.Empty<string>();
                if (model.Data.GetArray<string>("m_BodyGroupsHiddenInTools") is string[] hideBodyGroups)
                {
                    hideInTools = hideBodyGroups;
                }

                var groupedChoices = new Dictionary<string, List<(int ChoiceIndex, string FullName, string ChoiceName)>>();

                for (var i = 0; i < meshGroups!.Length; i++)
                {
                    var fullName = meshGroups[i];
                    var split = fullName.Split("_@");

                    if (split.Length < 2)
                    {
                        continue;
                    }

                    var groupName = split[0];
                    var choiceName = split[1];

                    groupedChoices.TryAdd(groupName, []);
                    groupedChoices[groupName].Add((i, fullName, choiceName));
                }

                foreach (var (groupName, choices) in groupedChoices)
                {
                    var choiceList = KVObject.Array();
                    var bodyGroup = MakeNode("BodyGroup",
                        ("name", groupName),
                        ("children", choiceList)
                    );

                    if (hideInTools.Contains(groupName))
                    {
                        bodyGroup.Add("hidden_in_tools", true);
                    }

                    var i = 0;
                    foreach (var (index, key, name) in choices)
                    {
                        var meshGroupChoice = MakeNode("BodyGroupChoice");

                        if (name != i.ToString(CultureInfo.InvariantCulture))
                        {
                            meshGroupChoice.Add("name", name);
                        }

                        if (hideInTools.Contains(key))
                        {
                            meshGroupChoice.Add("hide_in_tools", true);
                        }

                        var meshes = KVObject.Array();
                        meshGroupChoice.Add("meshes", meshes);

                        foreach (var renderMesh in RenderMeshesToExtract)
                        {
                            // No mask will show up as 'Empty' in editor
                            var mask = renderMesh.Index < meshGroupMasks.Length ? meshGroupMasks[renderMesh.Index] : 0UL;

                            if ((mask & 1UL << index) == 0)
                            {
                                continue;
                            }

                            meshes.Add(renderMesh.Name);
                        }

                        choiceList.Add(meshGroupChoice);
                        i++;
                    }

                    bodyGroupList.Value.Add(bodyGroup);
                }
            }

            var mesh = RenderMeshesToExtract.First();
            var attachments = mesh.Mesh.Attachments;

            foreach (var attachment in attachments.Values)
            {
                var mainInfluence = attachment[^1];

                var node = MakeNode("Attachment",
                    ("name", attachment.Name),
                    ("ignore_rotation", attachment.IgnoreRotation),
                    ("parent_bone", mainInfluence.Name),
                    ("relative_origin", ToKVArray(mainInfluence.Offset)),
                    ("relative_angles", ToKVArray(ToEulerAngles(mainInfluence.Rotation))),
                    ("weight", mainInfluence.Weight)
                );

                if (attachment.Length > 1)
                {
                    var children = KVObject.Array();
                    for (var i = 0; i < attachment.Length - 1; i++)
                    {
                        var influence = attachment[i];
                        var childNode = MakeNode("AttachmentInfluence",
                            ("parent_bone", influence.Name),
                            ("relative_origin", ToKVArray(influence.Offset)),
                            ("relative_angles", ToKVArray(ToEulerAngles(influence.Rotation))),
                            ("weight", influence.Weight)
                        );

                        children.Add(childNode);
                    }
                    node.Add("children", children);
                }

                attachmentList.Value.Add(node);
            }
        }

        var modelSequenceData = model?.Resource?.GetBlockByType(BlockType.ASEQ) as KeyValuesOrNTRO;
        var additionalSequenceData = new Dictionary<string, KVObject>();
        string[]? sequenceLocalReferenceArray = null;
        string[]? poseParamNames = null;

        if (modelSequenceData?.Data is KVObject sequenceData)
        {
            ExtractSequenceData(modelSequenceData);

            foreach (var data in sequenceData.GetArray("m_localS1SeqDescArray"))
            {
                additionalSequenceData.Add(data.GetStringProperty("m_sName"), data);
            }

            var poseParams = sequenceData.GetArray("m_localPoseParamArray");
            ExtractPoseParams(poseParams);

            poseParamNames = [.. poseParams.Select(x => x.GetStringProperty("m_sName"))];
            sequenceLocalReferenceArray = sequenceData.GetArray<string>("m_localSequenceNameArray");
        }

        if (AnimationsToExtract.Count > 0)
        {
            var animationToFolder = new Dictionary<string, KVObject>(AnimationsToExtract.Count);
            if (modelSequenceData?.Data.GetSubCollection("m_keyValues") is KVObject sequenceKeyValues)
            {
                if (sequenceKeyValues.GetSubCollection("faceposer_folders") is KVObject faceposerFolders)
                {
                    foreach (var (folderName, _) in faceposerFolders)
                    {
                        var animationNames = faceposerFolders.GetArray<string>(folderName);

                        var (folderNode, children) = MakeListNode("Folder");
                        folderNode.Add("name", folderName);
                        animationList.Value.Add(folderNode);

                        foreach (var animationName in animationNames!)
                        {
                            animationToFolder.Add(animationName, children);
                        }
                    }
                }
            }

            void AddToFolderOrRoot(string name, KVObject node)
            {
                var folderOrRoot = animationToFolder.GetValueOrDefault(name, animationList.Value);
                folderOrRoot.Add(node);
            }

            foreach (var (name, aseq) in additionalSequenceData)
            {
                var sequenceKeys = aseq.GetSubCollection("m_SequenceKeys");
                if (sequenceKeys == null)
                {
                    continue;
                }

                // Animations that have this property do not appear in Animations list.
                if (sequenceKeys.GetProperty<bool>("bind_pose"))
                {
                    var animBindPose = MakeNode(
                        "AnimBindPose",
                        ("name", name)
                    );

                    AddToFolderOrRoot(name, animBindPose);
                }
            }

            var sequences = AnimationsToExtract.Where(x => x.Anim.FromSequence);
            foreach (var animation in sequences)
            {
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

                if (animation.Anim.Activities.Length > 0)
                {
                    var activity = animation.Anim.Activities[0];
                    animationFile.Add("activity_name", activity.Name);
                    animationFile.Add("activity_weight", activity.Weight);
                }

                var childrenKV = KVObject.Array();
                if (animation.Anim.HasMovementData())
                {
                    var flags = animation.Anim.Movements[0].MotionFlags;
                    var extractMotion = MakeNode("ExtractMotion",
                        ("extract_tx", flags.HasFlag(ModelAnimationMotionFlags.TX)),
                        ("extract_ty", flags.HasFlag(ModelAnimationMotionFlags.TY)),
                        ("extract_tz", flags.HasFlag(ModelAnimationMotionFlags.TZ)),
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

                    if (animEvent.EventData != null)
                    {
                        animEventNode.Add("event_keys", animEvent.EventData);
                    }
                    childrenKV.Add(animEventNode);
                }

                if (sequenceLocalReferenceArray != null && poseParamNames != null)
                {
                    foreach (var autoLayer in animation.Anim.AutoLayers)
                    {
                        var layerNode = ProcessAnimationAutoLayer(animation.Anim, autoLayer, sequenceLocalReferenceArray, poseParamNames);
                        childrenKV.Add(layerNode);
                    }
                }

                if (animation.Anim.Autoplay)
                {
                    var autoLayer = MakeNode("AnimAutoLayer");
                    childrenKV.Add(autoLayer);
                }

                if (poseParamNames != null && animation.Anim.Fetch != null && animation.Anim.Fetch.Value.LocalCyclePoseParameter != -1)
                {
                    var poseParamIndex = animation.Anim.Fetch.Value.LocalCyclePoseParameter;
                    var poseParam = poseParamNames[poseParamIndex];

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

                if (additionalSequenceData.TryGetValue(animation.Anim.Name, out var animSequenceData))
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
                    }
                }

                if (childrenKV.Count > 0)
                {
                    animationFile.Add("children", childrenKV);
                }

                AddToFolderOrRoot(animation.Anim.Name, animationFile);
            }
        }

        if (PhysHullsToExtract.Count > 0 || PhysMeshesToExtract.Count > 0)
        {
            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                if (PhysicsToRenderMaterialNameProvider is null)
                {
                    RemapMaterials(null, globalReplace: true);
                }
                else
                {
                    var remapTable = SurfaceTagCombos.ToDictionary(
                        combo => combo.StringMaterial,
                        combo => PhysicsToRenderMaterialNameProvider(combo)
                    );
                    RemapMaterials(remapTable, globalReplace: false);
                }
            }

            foreach (var (physHull, fileName) in PhysHullsToExtract)
            {
                HandlePhysMeshNode(physHull, fileName);
            }

            foreach (var (physMesh, fileName) in PhysMeshesToExtract)
            {
                HandlePhysMeshNode(physMesh, fileName);
            }
        }

        if (model != null)
        {
            ExtractModelKeyValues(root.Node);
            ExtractHitboxSets();

            if (model.Skeleton.Roots.Length > 0)
            {
                AddBonesRecursive(model.Skeleton.Roots, skeleton.Value);
            }
        }

        if (physAggregateData is not null)
        {
            var boneNames = physAggregateData.Data.GetArray<string>("m_boneNames");
            boneNames ??= [];

            for (var i = 0; i < physAggregateData.Parts.Length; i++)
            {
                var physicsPart = physAggregateData.Parts[i];
                var parentBone = boneNames.Length > i ? boneNames[i] : string.Empty;

                foreach (var sphere in physicsPart.Shape.Spheres)
                {
                    var physicsShapeSphere = MakeNode(
                        "PhysicsShapeSphere",
                        ("parent_bone", parentBone),
                        ("surface_prop", PhysicsSurfaceNames[sphere.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[sphere.CollisionAttributeIndex])),
                        ("radius", sphere.Shape.Radius),
                        ("center", ToKVArray(sphere.Shape.Center)),
                        ("name", sphere.UserFriendlyName ?? string.Empty)
                    );

                    physicsShapeList.Value.Add(physicsShapeSphere);
                }

                foreach (var capsule in physicsPart.Shape.Capsules)
                {
                    var physicsShapeCapsule = MakeNode(
                        "PhysicsShapeCapsule",
                        ("parent_bone", parentBone),
                        ("surface_prop", PhysicsSurfaceNames[capsule.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[capsule.CollisionAttributeIndex])),
                        ("radius", capsule.Shape.Radius),
                        ("point0", ToKVArray(capsule.Shape.Center[0])),
                        ("point1", ToKVArray(capsule.Shape.Center[1])),
                        ("name", capsule.UserFriendlyName ?? string.Empty)
                    );

                    physicsShapeList.Value.Add(physicsShapeCapsule);
                }
            }
        }

        if (Translation != Vector3.Zero)
        {
            modelModifierList.Value.Add(MakeNode("ModelModifier_Translate", ("translation", ToKVArray(Translation))));
        }


        return new KV3File(kv, format: KV3IDLookup.Get("modeldoc28")).ToString();

        #region Local Functions
        void HandlePhysMeshNode<TShape>(ShapeDescriptor<TShape> shapeDesc, string fileName)
            where TShape : struct
        {
            var surfacePropName = PhysicsSurfaceNames[shapeDesc.SurfacePropertyIndex];
            var collisionTags = PhysicsCollisionTags[shapeDesc.CollisionAttributeIndex];

            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                renderMeshList.Value.Add(MakeNode("RenderMeshFile", ("filename", fileName)));
                return;
            }

            var className = shapeDesc switch
            {
                HullDescriptor => "PhysicsHullFile",
                MeshDescriptor => "PhysicsMeshFile",
                _ => throw new NotImplementedException()
            };

            var shapeName = shapeDesc.UserFriendlyName ?? Path.GetFileNameWithoutExtension(fileName);

            // TODO: per faceSet surface_prop
            var physicsShapeFile = MakeNode(
                className,
                ("filename", fileName),
                ("surface_prop", surfacePropName),
                ("collision_tags", string.Join(" ", collisionTags)),
                ("name", shapeName)
            );

            physicsShapeList.Value.Add(physicsShapeFile);
        }

        void RemapMaterials(
            IReadOnlyDictionary<string, string>? remapTable = null,
            bool globalReplace = false,
            string globalDefault = "materials/tools/toolsnodraw.vmat")
        {
            var remaps = KVObject.Array();
            materialGroupList.Value.Add(
                MakeNode(
                    "DefaultMaterialGroup",
                    ("remaps", remaps),
                    ("use_global_default", globalReplace),
                    ("global_default_material", globalDefault)
                )
            );

            if (globalReplace || remapTable == null)
            {
                return;
            }

            foreach (var (from, to) in remapTable)
            {
                var remap = KVObject.Collection();
                remap.Add("from", from);
                remap.Add("to", to);
                remaps.Add(remap);
            }
        }

        KVObject GetHitboxNode(Hitbox hitbox)
        {
            var node = hitbox.ShapeType switch
            {
                Hitbox.HitboxShape.Box => MakeNode("Hitbox",
                    ("hitbox_mins", ToKVArray(hitbox.MinBounds)),
                    ("hitbox_maxs", ToKVArray(hitbox.MaxBounds))
                ),
                Hitbox.HitboxShape.Capsule => MakeNode("HitboxCapsule",
                    ("radius", hitbox.ShapeRadius),
                    ("point0", ToKVArray(hitbox.MinBounds)),
                    ("point1", ToKVArray(hitbox.MaxBounds))
                ),
                Hitbox.HitboxShape.Sphere => MakeNode("HitboxSphere",
                    ("center", ToKVArray(hitbox.MinBounds)),
                    ("radius", hitbox.ShapeRadius)
                ),
                _ => throw new NotImplementedException($"Unknown hitbox shape type: {hitbox.ShapeType}")
            };

            node.Add("name", hitbox.Name);
            node.Add("parent_bone", hitbox.BoneName);
            node.Add("surface_property", hitbox.SurfaceProperty);
            node.Add("translation_only", hitbox.TranslationOnly);
            node.Add("group_id", hitbox.GroupId);

            return node;
        }

        void ExtractHitboxSets()
        {
            if (model.HitboxSets == null)
            {
                return;
            }

            foreach (var pair in model.HitboxSets)
            {
                var children = KVObject.Array();
                var hitboxSet = MakeNode("HitboxSet", ("name", pair.Key), ("children", children));

                foreach (var hitbox in pair.Value)
                {
                    var hitboxNode = GetHitboxNode(hitbox);
                    children.Add(hitboxNode);
                }

                hitboxSetList.Value.Add(hitboxSet);
            }
        }

        void ExtractSequenceData(KeyValuesOrNTRO sequenceData)
        {
            var boneMasks = sequenceData.Data.GetArray("m_localBoneMaskArray");
            var boneNames = sequenceData.Data.GetArray<string>("m_localBoneNameArray");

            foreach (var boneMask in boneMasks!)
            {
                var name = boneMask.GetProperty<string>("m_sName");
                var boneArray = boneMask.GetIntegerArray("m_nLocalBoneArray");
                var boneWeights = boneMask.GetFloatArray("m_flBoneWeightArray");
                // master_morph_weight = m_flDefaultMorphCtrlWeight

                // skip default mask
                if (name == "default" && boneArray.Length == 0)
                {
                    continue;
                }

                var weights = KVObject.Array();
                var weightListNode = MakeNode("WeightList",
                    ("name", name),
                    ("weights", weights)
                );

                foreach (var (boneIndex, boneWeight) in boneArray.Zip(boneWeights))
                {
                    var weightDefinition = KVObject.Collection();
                    var boneName = boneNames![boneIndex];

                    weightDefinition.Add("bone", boneName);
                    weightDefinition.Add("weight", boneWeight);
                    weights.Add(weightDefinition);
                }

                weightLists.Value.Add(weightListNode);
            }
        }


        void ExtractPoseParams(KVObject[] poseParamsData)
        {
            foreach (var poseParam in poseParamsData)
            {
                var name = poseParam.GetProperty<string>("m_sName");
                var start = poseParam.GetFloatProperty("m_flStart");
                var end = poseParam.GetFloatProperty("m_flEnd");
                var loop = poseParam.GetFloatProperty("m_flLoop");
                var looping = poseParam.GetProperty<bool>("m_bLooping");

                var poseParamNode = MakeNode("PoseParam",
                    ("name", name),
                    ("poseparam_min", start),
                    ("poseparam_max", end),
                    ("poseparam_looping", looping),
                    ("poseparam_loop", loop)
                );

                poseParamList.Value.Add(poseParamNode);
            }
        }

        void ExtractModelKeyValues(KVObject rootNode)
        {
            if (model.Data.ContainsKey("m_refAnimIncludeModels"))
            {
                foreach (var animIncludeModel in model.Data.GetArray<string>("m_refAnimIncludeModels")!)
                {
                    animationList.Value.Add(MakeNode("AnimIncludeModel", ("model", animIncludeModel)));
                }
            }

            if (model.Data.ContainsKey("m_vecNmSkeletonRefs"))
            {
                foreach (var skeletonRef in model.Data.GetArray<string>("m_vecNmSkeletonRefs"))
                {
                    nmskelList.Value.Add(MakeNode("NmSkeletonReference", ("filename", skeletonRef)));
                }
            }

            if (model.Data.ContainsKey("m_animGraph2Refs"))
            {
                var animGraph2Refs = model.Data.GetArray("m_animGraph2Refs");
                for (int i = 0; i < animGraph2Refs.Length; i++)
                {
                    var refObj = animGraph2Refs[i];
                    var identifier = refObj.GetStringProperty("m_sIdentifier");
                    var graphPath = refObj.GetStringProperty("m_hGraph");

                    if (i == 0)
                    {
                        animGraph2List.Value.Add(MakeNode("DefaultAnimGraph2", ("filename", graphPath)));
                    }
                    else
                    {
                        animGraph2List.Value.Add(MakeNode("AnimGraph2", ("name", identifier), ("filename", graphPath)));
                    }
                }
            }

            var breakPieceList = MakeLazyList("BreakPieceList");
            var gameDataList = MakeLazyList("GameDataList");

            var keyvalues = model.KeyValues;

            if (keyvalues.Count == 0)
            {
                return;
            }

            if (keyvalues.ContainsKey("anim_graph_resource"))
            {
                rootNode.Add("anim_graph_name", keyvalues.GetProperty<string>("anim_graph_resource"));
            }

            if (keyvalues.ContainsKey("BoneConstraintList"))
            {
                var boneConstraintListData = keyvalues.GetArray("BoneConstraintList");
                var boneConstraintList = ExtractBoneConstraints(boneConstraintListData);
                root.Children.Add(boneConstraintList);
            }

            var genericDataClasses = new string[] {
                "prop_data",
                "character_arm_config",
                "vr_carry_type",
                "door_sounds",
                "nav_data",
                "npc_foot_sweep",
                "ai_model_info",
                "breakable_door_model",
                "dynamic_interactions",
                "explosion_behavior",
                "eye_occlusion_renderer",
                "fire_interactions",
                "gastank_markup",
                "hand_conform_data",
                "handpose_data",
                "physgun_interactions",
                "weapon_metadata",
                "glove_viewmodel_reference",
                "composite_material_order",
                "patch_camera_preset_list",
                "camera_settings",
                "scene_data_map",
                "particle_settings",
                "damage_number_settings",
                "CitadelCameraSettings_t",
                "CCitadelHeroModelGameData_t",
                "CCitadelNPCModelGameData_t",
                "CitadelUnitStatusSettings_t",
                "CitadelModelDamageNumberSettings_t",
                "CitadelModelParticleSettings_t",
                "CitadelTaggedSoundSettings_t",
                "CitadelModelSceneData_t",
                "CitadelMuzzleSettings_t",
                "CitadelTeamRelativeParticleSettings_t",
                "CitadelEventIDToBodyGroupMapping_t",
                //"AttachmentCameraData", - is autogenerated from AttachmentCameraPreview/ExporttoRuntimeModel modeldoc node/parameter
                "CDestructiblePart",
                "CDestructiblePartsSystemData",
                "DeformablePropModelGameData_t",
                "CPhysicsBodyGameMarkupData",
                "electrical_interactions",
                "world_interactions",
            };

            var genericDataClassesList = new (string ListKey, string Class)[] {
            ("ao_proxy_capsule_list", "ao_proxy_capsule"),
            ("ao_proxy_box_list", "ao_proxy_box"),
            ("particles_list", "particle"),
            ("hand_pose_list", "hand_pose_pair"),
            ("eye_data_list", "eye"),
            ("bodygroup_driven_morph_list", "bodygroup_driven_morph"),
            ("materialgroup_driven_morph_list", "materialgroup_driven_morph"),
            ("animating_breakable_stage_list", "animating_breakable_stage"),
            ("cables_list", "cable"),
            ("high_quality_shadows_region_list", "high_quality_shadows_region"),
            ("particle_cfg_list", "particle_cfg"),
            ("snapshot_weights_upperbody_list", "snapshot_weights_upperbody"),
            ("snapshot_weights_all_list", "snapshot_weights_all"),
            ("bodygroup_preset_list", "bodygroup_preset"),
            ("muzzle_desc_list", "muzzle_settings"),
            ("unit_status_settings_list", "unit_status_settings"),
            ("team_relative_particles_cfg_list", "team_relative_particle_settings"),
            ("CNPCPhysicsHull", "CNPCPhysicsHull"), // exports as list, needs m_sName changed to name near game_class
        };

            foreach (var genericDataClass in genericDataClasses)
            {
                if (keyvalues.ContainsKey(genericDataClass))
                {
                    var genericData = keyvalues.GetProperty<KVObject>(genericDataClass);
                    if (genericData != null)
                    {
                        AddGenericGameData(gameDataList.Value, genericDataClass, genericData);
                    }
                }
            }

            foreach (var genericDataClass in genericDataClassesList)
            {
                var dataKey = genericDataClass.ListKey;
                if (keyvalues.ContainsKey(dataKey))
                {
                    var genericDataList = keyvalues.GetArray(dataKey);
                    foreach (var genericData in genericDataList!)
                    {
                        AddGenericGameData(gameDataList.Value, genericDataClass.Class, genericData);
                    }
                }
            }

            if (keyvalues.ContainsKey("LookAtList"))
            {
                var lookAtList = keyvalues.GetSubCollection("LookAtList");
                foreach (var (_, item) in lookAtList)
                {
                    if (item.ValueType == KVValueType.Collection)
                    {
                        AddGenericGameData(gameDataList.Value, "LookAtChain", item, "lookat_chain");
                    }
                }
            }

            if (keyvalues.ContainsKey("MovementSettings"))
            {
                var movementSettings = keyvalues.GetProperty<KVObject>("MovementSettings");
                AddGenericGameData(gameDataList.Value, "MovementSettings", movementSettings, "movementsettings");
            }

            if (keyvalues.ContainsKey("FeetSettings"))
            {
                var feetSettings = keyvalues.GetProperty<KVObject>("FeetSettings");
                var feetNode = ConvertFeetSettings(feetSettings!);
                if (feetNode != null)
                {
                    gameDataList.Value.Add(feetNode);
                }
            }

            if (keyvalues.ContainsKey("break_list"))
            {
                foreach (var breakPiece in keyvalues.GetArray("break_list")!)
                {
                    var breakPieceFile = MakeNode("BreakPieceExternal", breakPiece);
                    breakPieceList.Value.Add(breakPieceFile);
                }
            }

            static KVObject? ConvertFeetSettings(KVObject feetSettings)
            {
                var children = KVObject.Array();

                // Field mappings from compiled to source names
                var footFieldMappings = new (string CompiledName, string SourceName)[]
                {
                    ("m_name", "name"),
                    ("m_ankleBoneName", "anklebone"),
                    ("m_toeBoneName", "toebone"),
                    ("m_vBallOffset", "balloffset"),
                    ("m_vHeelOffset", "heeloffset"),
                    ("m_flTraceHeight", "traceheight"),
                    ("m_flTraceRadius", "traceradius"),
                };

                // Convert each foot entry to a Foot child node
                foreach (var (_, footEntry) in feetSettings.Children)
                {
                    if (footEntry.ValueType != KVValueType.Collection)
                    {
                        continue;
                    }

                    var footNode = MakeNode("Foot");

                    // Map compiled field names to source field names
                    foreach (var (compiledName, sourceName) in footFieldMappings)
                    {
                        if (footEntry.ContainsKey(compiledName))
                        {
                            footNode.Add(sourceName, footEntry[compiledName]);
                        }
                    }

                    // autolevel is typically true by default in source format
                    footNode.Add("autolevel", true);

                    children.Add(footNode);
                }

                if (children.Count == 0)
                {
                    return null;
                }

                // Create the Feet node
                var feetNode = MakeNode("Feet", ("children", children));

                // Parent-level field mappings
                var parentFieldMappings = new (string CompiledName, string SourceName)[]
                {
                    ("m_flLockTolerance", "locktolerance"),
                    ("m_flHeightTolerance", "heighttolerance"),
                    ("m_bSanitizeTrajectories", "sanitizetrajectories"),
                };

                // Add parent-level properties if they exist
                foreach (var (compiledName, sourceName) in parentFieldMappings)
                {
                    if (feetSettings.ContainsKey(compiledName))
                    {
                        feetNode.Add(sourceName, feetSettings[compiledName]);
                    }
                }

                return feetNode;
            }

            static void AddGenericGameData(KVObject gameDataList, string genericDataClass, KVObject? genericData, string? dataKey = null)
            {
                if (genericData is null)
                {
                    return;
                }

                // Remove quotes from keys by rebuilding the object
                var cleanedData = KVObject.Collection();
                foreach (var (key, value) in genericData.Children)
                {
                    var trimmed = key?.Trim('"') ?? string.Empty;
                    cleanedData.Add(trimmed, value);
                }

                var name = "";
                if (cleanedData.ContainsKey("name"))
                {
                    name = cleanedData.GetStringProperty("name");
                }

                KVObject genericGameData;
                if (dataKey == null)
                {
                    genericGameData = MakeNode("GenericGameData",
                        ("name", name),
                        ("game_class", genericDataClass),
                        ("game_keys", cleanedData)
                    );
                }
                else
                {
                    genericGameData = MakeNode(genericDataClass,
                        ("name", name),
                        (dataKey, cleanedData)
                    );
                }

                gameDataList.Add(genericGameData);
            }
        }
        #endregion
    }
}
