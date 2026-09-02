using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes that make up the rig: the bone hierarchy, the constraints that drive
/// one bone from another, and both IK systems.
/// </summary>
partial class ModelExtract
{
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

    static string? RemapIKJointConstraintClassname(string className)
    {
        return className switch
        {
            "CIKJointConstraintData_Hinge" => "IKJointConstraint_Hinge",
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
                var angles = EntityTransformHelper.ToEulerAngles(rot);
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

    KVObject? ProcessBoneConstraintTarget(KVObject target)
    {
        var isAttachment = target.GetBooleanProperty("m_bIsAttachment");
        var targetHash = target.GetUInt32Property("m_nBoneHash");
        if (!StringToken.InvertedTable.TryGetValue(targetHash, out var targetName))
        {
            ProgressReporter?.Report($"Skipping a bone constraint: no name for {(isAttachment ? "attachment" : "bone")} {targetHash}.");
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

    KVObject? ProcessBoneConstraintSlave(KVObject slave)
    {
        var boneHash = slave.GetUInt32Property("m_nBoneHash");
        if (!StringToken.InvertedTable.TryGetValue(boneHash, out var boneName))
        {
            ProgressReporter?.Report($"Skipping a bone constraint: no name for bone {boneHash}.");
            return null;
        }

        var node = MakeNode("AnimConstraintSlave", ("parent_bone", boneName));
        AddBoneConstraintProperty<double>(slave, node, "m_flWeight", "weight");
        AddBoneConstraintProperty<Vector3>(slave, node, "m_vBasePosition", "relative_origin");
        AddBoneConstraintProperty<Quaternion>(slave, node, "m_qBaseOrientation", "relative_angles");
        return node;
    }

    void ProcessBoneConstraintChildren(KVObject boneConstraint, KVObject node)
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

            // This field is in RadianEuler component order (roll, pitch, yaw), the reverse of the
            // QAngle order (pitch, yaw, roll) that ToEulerAngles returns
            var rotArray = constrainedBoneData.GetFloatArray("m_qBaseOrientation");
            var rot = new Quaternion(rotArray[0], rotArray[1], rotArray[2], rotArray[3]);
            var angles = EntityTransformHelper.ToEulerAngles(rot);
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

    KVObject? ProcessBoneConstraint(BoneConstraint constraint)
    {
        var boneConstraint = constraint.Data;
        var className = constraint.ClassName;
        var targetClassName = RemapBoneConstraintClassname(className);
        if (targetClassName == null)
        {
            ProgressReporter?.Report($"Skipping bone constraint of unknown type {className}.");
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

    KVObject ExtractBoneConstraints(Model model)
    {
        var stringTokenKeys = model.Skeleton.Bones.Select(b => b.Name);
        if (RenderMeshesToExtract.Count > 0)
        {
            var mesh = RenderMeshesToExtract.First().Mesh;
            stringTokenKeys = stringTokenKeys.Concat(mesh.Attachments.Keys);
        }

        StringToken.Store(stringTokenKeys);

        var childrenKV = KVObject.Array();

        foreach (var boneConstraint in model.BoneConstraints)
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
    /// <summary>
    /// Builds the IKData node from both IK systems, or returns null when the model carries
    /// neither. A chain that kept no joints is left out, because the compiler rejects one.
    /// </summary>
    static KVObject? BuildIKData(Model model)
    {
        var childrenKV = KVObject.Array();

        foreach (var ikChain in AnimGraphModelInfo.GetIKChainsFromModel(model) ?? [])
        {
            if (GetIKChainJoints(ikChain).Count == 0)
            {
                continue;
            }

            childrenKV.Add(BuildIKChain(ikChain));
        }

        if (AnimGraphModelInfo.GetIKControlRigFromModel(model) is { } controlRig)
        {
            childrenKV.Add(BuildIKRig(controlRig.Rig, controlRig.Chains));
        }

        return childrenKV.Count > 0 ? MakeNode("IKData", ("children", childrenKV)) : null;
    }

    static IReadOnlyList<KVObject> GetIKChainJoints(KVObject ikChain)
        => ikChain.ContainsKey("m_Joints") ? ikChain.GetArray("m_Joints") : [];

    /// <summary>
    /// Builds the legacy IK rig node. Its chains carry the whole IK definition for models authored
    /// before chains moved into m_IKChains, where the m_IKChains entries are left without joints.
    /// </summary>
    static KVObject BuildIKRig(KVObject rig, IReadOnlyList<KVObject> chainData)
    {
        var childrenKV = KVObject.Array();

        foreach (var chain in chainData)
        {
            childrenKV.Add(BuildIKRigChain(chain));
        }

        var rigNode = MakeNode("IKRigSimple", ("name", "ik_rig"), ("children", childrenKV));

        AddIfPresent(rigNode, "system", rig, "m_SystemType");
        AddIfPresent(rigNode, "initial_master_blend_amount", rig, "m_flInitialMasterBlendAmount");
        AddIfPresent(rigNode, "default_tilt_spring_strength", rig, "m_flDefaultTiltSpringStrength");
        AddIfPresent(rigNode, "abs_origin_drop_height", rig, "m_flAbsOriginDropHeight");
        AddIfPresent(rigNode, "abs_origin_drop_height_spring_strength", rig, "m_flAbsOriginDropSpringStrength");
        AddIfPresent(rigNode, "animgraph_master_blend_parameter_name", rig, "m_MasterBlendAnimgraphParameterName");

        if (rig.ContainsKey("m_TiltBone"))
        {
            rigNode.Add("tilt_bone", GetBoneReferenceName(rig, "m_TiltBone"));
        }

        return rigNode;
    }

    static KVObject BuildIKRigChain(KVObject chainData)
    {
        var chainNode = MakeNode("IKChainOld", ("name", chainData.GetStringProperty("m_Name", string.Empty)));

        var childrenKV = KVObject.Array();

        if (chainData.ContainsKey("m_JointConstraintPairs"))
        {
            foreach (var pair in chainData.GetArray("m_JointConstraintPairs"))
            {
                var constraint = BuildIKRigJointConstraint(pair);
                if (constraint != null)
                {
                    childrenKV.Add(constraint);
                }
            }
        }

        if (chainData.ContainsKey("m_RuleData"))
        {
            foreach (var rule in chainData.GetArray("m_RuleData"))
            {
                var ruleNode = BuildIKRigRule(rule);
                if (ruleNode != null)
                {
                    childrenKV.Add(ruleNode);
                }
            }
        }

        if (childrenKV.Count > 0)
        {
            chainNode.Add("children", childrenKV);
        }

        chainNode.Add("root_bone", GetBoneReferenceName(chainData, "m_RootBone"));
        chainNode.Add("end_effector_bone", GetBoneReferenceName(chainData, "m_EndEffectorBone"));
        chainNode.Add("end_effector_target_bone", GetBoneReferenceName(chainData, "m_EndEffectorTargetBone"));
        chainNode.Add("reverse_footlock_bone", GetBoneReferenceName(chainData, "m_ReverseFootLockBone"));
        AddIfPresent(chainNode, "solver", chainData, "m_SolverType");
        AddIfPresent(chainNode, "break_restoration_time", chainData, "m_flBreakRestorationTime");
        AddIfPresent(chainNode, "max_lock_distance_to_target", chainData, "m_flMaxLockDistanceToTarget");
        AddIfPresent(chainNode, "use_target_instead_of_lock_threshold", chainData, "m_flUseTargetInsteadOfLockThreshold");
        AddIfPresent(chainNode, "soften_percentage", chainData, "m_flSoftenPercentage");
        AddIfPresent(chainNode, "soften_time", chainData, "m_flSoftenTime");
        AddIfPresent(chainNode, "hyperextension_release_dot_threshold", chainData, "m_flHyperExtensionLockReleaseDotThreshold");

        return chainNode;
    }

    static KVObject? BuildIKRigJointConstraint(KVObject pair)
        => pair.ContainsKey("m_pJointConstraintData")
            ? BuildIKJointConstraint(pair.GetSubCollection("m_pJointConstraintData"), GetBoneReferenceName(pair, "m_Bone"))
            : null;

    static KVObject? BuildIKRigRule(KVObject rule)
    {
        if (rule.GetStringProperty("_class", string.Empty) != "CIKRuleData_Ground_VirtualPlanes")
        {
            return null;
        }

        var ruleNode = MakeNode("IKRuleGround", ("name", "ground"));
        AddIfPresent(ruleNode, "trace_height", rule, "m_flRaycastHeight");
        AddIfPresent(ruleNode, "trace_radius", rule, "m_flRaycastRadius");
        AddIfPresent(ruleNode, "z_spring_strength", rule, "m_flZSpringStiffness");
        AddIfPresent(ruleNode, "normal_spring_strength", rule, "m_flNormalSpringStiffness");

        return ruleNode;
    }

    static KVObject BuildIKChain(KVObject ikChain)
    {
        var chainNode = MakeNode("IKChain", ("name", ikChain.GetStringProperty("m_Name", string.Empty)));

        var joints = GetIKChainJoints(ikChain);
        if (joints.Count > 0)
        {
            var childrenKV = KVObject.Array();
            childrenKV.Add(BuildIKChainJoint(joints, 0));
            chainNode.Add("children", childrenKV);
        }

        var solverSettings = ikChain.GetSubCollection("m_DefaultSolverSettings");
        var targetSettings = ikChain.GetSubCollection("m_DefaultTargetSettings");

        // Only these exact spellings are read back; anything else compiles and is then ignored.
        // Setting the solver type also forces the rotation fix up mode to None, hence that fallback.
        AddIfPresent(chainNode, "m_bDoBonesOrientAlongPositiveX", ikChain, "m_bDoBonesOrientAlongPositiveX");
        AddIfPresent(chainNode, "m_DefaultSolverSettings.m_nNumIterations", solverSettings, "m_nNumIterations");
        AddIfPresent(chainNode, "m_DefaultSolverSettings.m_SolverType ", solverSettings, "m_SolverType");
        AddIfPresent(chainNode, "m_DefaultSolverSettings.m_EndEffectorRotationFixUpMode", solverSettings, "m_EndEffectorRotationFixUpMode");
        chainNode.Add("m_DefaultTargetSettings.m_Bone", MakeNamedReference(targetSettings, "m_Bone"));
        AddIfPresent(chainNode, "m_DefaultTargetSettings.m_TargetSource", targetSettings, "m_TargetSource");
        chainNode.Add("m_Data.m_DefaultTargetSettings.m_AnimgraphParameterNamePosition", MakeAnimParamReference(targetSettings, "m_AnimgraphParameterNamePosition"));
        chainNode.Add("m_Data.m_DefaultTargetSettings.m_AnimgraphParameterNameOrientation", MakeAnimParamReference(targetSettings, "m_AnimgraphParameterNameOrientation"));
        chainNode.Add("m_Data.m_EndEffectorFixedOffsetAttachment", MakeNamedReference(ikChain, "m_EndEffectorFixedOffsetAttachment"));
        AddIfPresent(chainNode, "m_Data.m_bParentJointRequiresAlignment", ikChain, "m_bParentJointRequiresAlignment");
        AddIfPresent(chainNode, "m_bUseNewPoleVectorForAxis", ikChain, "m_bUseNewPoleVectorForAxis");
        AddIfPresent(chainNode, "m_PoleVectorForAxis ", ikChain, "m_PoleVectorForAxis");

        return chainNode;
    }

    static KVObject BuildIKChainJoint(IReadOnlyList<KVObject> joints, int index)
    {
        var joint = joints[index];
        var boneName = GetBoneReferenceName(joint, "m_Bone");

        var jointNode = MakeNode("IKChainJoint", ("name", boneName));

        var childrenKV = KVObject.Array();

        if (index + 1 < joints.Count)
        {
            childrenKV.Add(BuildIKChainJoint(joints, index + 1));
        }

        if (joint.ContainsKey("m_JointConstraintData"))
        {
            foreach (var constraintData in joint.GetArray("m_JointConstraintData"))
            {
                var constraint = BuildIKJointConstraint(constraintData);
                if (constraint != null)
                {
                    childrenKV.Add(constraint);
                }
            }
        }

        if (childrenKV.Count > 0)
        {
            jointNode.Add("children", childrenKV);
        }

        if (!string.IsNullOrEmpty(boneName))
        {
            jointNode.Add("bone", boneName);
        }

        return jointNode;
    }

    static KVObject? BuildIKJointConstraint(KVObject constraintData, string constrainedJoint = "")
    {
        var className = RemapIKJointConstraintClassname(constraintData.GetStringProperty("_class", string.Empty));
        if (className == null)
        {
            return null;
        }

        var constraintNode = MakeNode(className, ("constrained_joint", constrainedJoint));
        constraintNode.Add("hinge_axis", constraintData.GetStringProperty("m_HingeAxis", string.Empty));
        constraintNode.Add("min_radians", constraintData.GetFloatProperty("m_flMinRadians"));
        constraintNode.Add("max_radians", constraintData.GetFloatProperty("m_flMaxRadians"));

        return constraintNode;
    }

    /// <summary>
    /// Copies a key across only when the compiled block carries it, so a field an older compiler
    /// era never wrote stays absent instead of being created at its default.
    /// </summary>
    static void AddBonesRecursive(IEnumerable<Bone> bones, KVObject parent)
    {
        foreach (var bone in bones)
        {
            var boneDefinitionNode = MakeNode(
                "Bone",
                ("name", GetExportBoneName(bone)),
                ("origin", ToKVArray(bone.Position)),
                ("angles", ToKVArray(EntityTransformHelper.ToEulerAngles(bone.Angle))),
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

    private static void AddWeightListNodes(ModelDocLists lists, KeyValuesOrNTRO sequenceData)
    {
        var boneMasks = sequenceData.Data.GetArray("m_localBoneMaskArray");
        var boneNames = sequenceData.Data.GetArray<string>("m_localBoneNameArray");

        foreach (var boneMask in boneMasks!)
        {
            var name = boneMask.GetStringProperty("m_sName");
            var boneArray = boneMask.GetIntegerArray("m_nLocalBoneArray");
            var boneWeights = boneMask.GetFloatArray("m_flBoneWeightArray");
            var masterMorphWeight = boneMask.GetFloatProperty("m_flDefaultMorphCtrlWeight", 1f);
            var morphCtrlWeightArray = boneMask.GetArray("m_morphCtrlWeightArray");

            // skip a default mask that carries nothing but its schema defaults
            if (name == "default" && boneArray.Length == 0 && masterMorphWeight == 1f
                && (morphCtrlWeightArray == null || morphCtrlWeightArray.Count == 0))
            {
                continue;
            }

            var weights = KVObject.Array();
            var morphWeights = KVObject.Array();
            var weightListNode = MakeNode("WeightList",
                ("name", name),
                ("weights", weights),
                ("master_morph_weight", masterMorphWeight),
                ("morph_weights", morphWeights)
            );

            foreach (var (boneIndex, boneWeight) in boneArray.Zip(boneWeights))
            {
                var weightDefinition = KVObject.Collection();
                var boneName = boneNames![boneIndex];

                weightDefinition.Add("bone", boneName);
                weightDefinition.Add("weight", boneWeight);
                weights.Add(weightDefinition);
            }

            foreach (var morphWeightPair in morphCtrlWeightArray ?? [])
            {
                var morphWeightDefinition = KVObject.Collection();

                morphWeightDefinition.Add("morph", (string)morphWeightPair[0]);
                morphWeightDefinition.Add("weight", (float)morphWeightPair[1]);
                morphWeights.Add(morphWeightDefinition);
            }

            lists.WeightLists.Add(weightListNode);
        }
    }

    private void AddScaleSetNodes(ModelDocLists lists, KeyValuesOrNTRO sequenceData)
    {
        var scaleSets = sequenceData.Data.GetArray("m_localScaleSetArray");

        if (scaleSets == null || scaleSets.Count == 0)
        {
            return;
        }

        var boneNames = sequenceData.Data.GetArray<string>("m_localBoneNameArray");
        var bonesByName = model?.Skeleton.Bones.ToDictionary(static bone => bone.Name);

        foreach (var scaleSet in scaleSets)
        {
            var boneArray = scaleSet.GetIntegerArray("m_nLocalBoneArray");
            var boneScaleArray = scaleSet.GetFloatArray("m_flBoneScaleArray");
            var rootOffsetArray = scaleSet.GetFloatArray("m_vRootOffset");
            var rootOffset = new Vector3(rootOffsetArray[0], rootOffsetArray[1], rootOffsetArray[2]);

            // The compiler divides each bone's authored scale by its nearest ancestor's authored
            // scale (within this same scale set, defaulting to 1 with no such ancestor), so the
            // compiled value is a scale relative to the set's own nearest scaled ancestor rather
            // than an independent per-bone multiplier. Recover the authored value by inverting that
            // walk up the skeleton, memoized since a deep chain revisits the same ancestors.
            var compiledScaleByBone = new Dictionary<string, float>(boneArray.Length);

            for (var i = 0; i < boneArray.Length; i++)
            {
                compiledScaleByBone[boneNames![boneArray[i]]] = boneScaleArray[i];
            }

            var authoredScaleByBone = new Dictionary<string, float>(boneArray.Length);

            float GetAuthoredScale(string boneName)
            {
                if (authoredScaleByBone.TryGetValue(boneName, out var cached))
                {
                    return cached;
                }

                var parentScale = 1f;
                var ancestor = bonesByName?.GetValueOrDefault(boneName)?.Parent;

                while (ancestor != null)
                {
                    if (compiledScaleByBone.ContainsKey(ancestor.Name))
                    {
                        parentScale = GetAuthoredScale(ancestor.Name);
                        break;
                    }

                    ancestor = ancestor.Parent;
                }

                var authored = compiledScaleByBone[boneName] * parentScale;
                authoredScaleByBone[boneName] = authored;

                return authored;
            }

            var scales = KVObject.Array();

            foreach (var boneIndex in boneArray)
            {
                var boneName = boneNames![boneIndex];
                var scaleDefinition = KVObject.Collection();
                scaleDefinition.Add("bone", boneName);
                scaleDefinition.Add("scale", GetAuthoredScale(boneName));
                scales.Add(scaleDefinition);
            }

            lists.ScaleSets.Add(MakeNode("ScaleSet",
                ("name", scaleSet.GetStringProperty("m_sName")),
                ("root_offset", ToKVArray(rootOffset)),
                ("scales", scales)
            ));
        }
    }

    private static void AddPoseParamNodes(ModelDocLists lists, IReadOnlyList<KVObject> poseParamsData)
    {
        foreach (var poseParam in poseParamsData)
        {
            var name = poseParam.GetStringProperty("m_sName");
            var start = poseParam.GetFloatProperty("m_flStart");
            var end = poseParam.GetFloatProperty("m_flEnd");
            var loop = poseParam.GetFloatProperty("m_flLoop");
            var looping = poseParam.GetBooleanProperty("m_bLooping");

            var poseParamNode = MakeNode("PoseParam",
                ("name", name),
                ("poseparam_min", start),
                ("poseparam_max", end),
                ("poseparam_looping", looping),
                ("poseparam_loop", loop)
            );

            lists.PoseParams.Add(poseParamNode);
        }
    }

    /// <summary>
    /// Writes the rig the model was authored against: its animation graph name, its bone constraints and
    /// its IK data.
    /// </summary>
    private void AddRigNodes(Model model, KVObject keyvalues, ModelDocLists lists, KVObject rootNode)
    {
        if (keyvalues.ContainsKey("anim_graph_resource"))
        {
            rootNode.Add("anim_graph_name", keyvalues.GetStringProperty("anim_graph_resource"));
        }

        if (model.BoneConstraints.Count > 0)
        {
            lists.RootChildren.Add(ExtractBoneConstraints(model));
        }

        if (BuildIKData(model) is { } ikData)
        {
            lists.RootChildren.Add(ikData);
        }
    }
}
