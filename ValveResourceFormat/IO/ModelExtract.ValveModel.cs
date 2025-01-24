using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    #region KV Helpers
    static KVValue MakeValue(object value)
    {
        var specialType = value switch
        {
            KVValue v => v,
            Vector3 vec3 => MakeArrayValue(new[] { vec3.X, vec3.Y, vec3.Z }),
            _ => null
        };

        if (specialType != null)
        {
            return specialType;
        }

        var basicType = value switch
        {
            string => KVType.STRING,
            bool => KVType.BOOLEAN,
            int => KVType.INT32,
            long => KVType.INT64,
            float => KVType.FLOAT,
            double => KVType.DOUBLE,
            KVObject kv => kv.IsArray ? KVType.ARRAY : KVType.OBJECT,
            _ => throw new NotImplementedException()
        };

        return new KVValue(basicType, value);
    }

    static KVValue MakeArrayValue<T>(IEnumerable<T> values)
    {
        var list = new KVObject(null, isArray: true);
        foreach (var value in values)
        {
            list.AddProperty(null, MakeValue(value));
        }

        return MakeValue(list);
    }

    static void AddItem(KVObject node, KVObject item)
    {
        Debug.Assert(node.IsArray);
        node.AddProperty(null, MakeValue(item));
    }

    static KVObject MakeNode(string className, params (string Name, object Value)[] properties)
    {
        var node = new KVObject(className);
        node.AddProperty("_class", MakeValue(className));
        foreach (var prop in properties)
        {
            node.AddProperty(prop.Name, MakeValue(prop.Value));
        }
        return node;
    }
    static (KVObject Node, KVObject Children) MakeListNode(string className)
    {
        var children = new KVObject(null, isArray: true);
        var node = MakeNode(className, ("children", children));
        return (node, children);
    }
    #endregion

    #region Bone Constraints
    static string RemapBoneConstraintClassname(string className)
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
                targetObject.AddProperty(targetName, MakeValue(angles));
            }
            else if (typeof(T) == typeof(Vector3))
            {
                var value = sourceObject.GetFloatArray(sourceName);
                var pos = new Vector3(value[0], value[1], value[2]);
                targetObject.AddProperty(targetName, MakeValue(pos));
            }
            else
            {
                var value = sourceObject.GetProperty<T>(sourceName);
                targetObject.AddProperty(targetName, MakeValue(value));
            }
        }
    }

    static KVObject ProcessBoneConstraintTarget(KVObject target)
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

    static KVObject ProcessBoneConstraintSlave(KVObject slave)
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
                                    .Where(p => p != null);

        IEnumerable<KVObject> children;
        if (node.GetStringProperty("_class") == "AnimConstraintParent")
        {
            //Parent constrants only have a single slave and it's not a child node in the .vmdl
            children = targets;

            var constrainedBoneData = boneConstraint.GetArray("m_slaves")[0];
            AddBoneConstraintProperty<double>(constrainedBoneData, node, "m_flWeight", "weight");
            AddBoneConstraintProperty<Vector3>(constrainedBoneData, node, "m_vBasePosition", "translation_offset");

            //Order of angles is different for some reason
            var rotArray = constrainedBoneData.GetFloatArray("m_qBaseOrientation");
            var rot = new Quaternion(rotArray[0], rotArray[1], rotArray[2], rotArray[3]);
            var angles = ToEulerAngles(rot);
            angles = new Vector3(angles.Z, angles.X, angles.Y);
            node.AddProperty("rotation_offset_xyz", MakeValue(angles));
        }
        else
        {
            var slaves = boneConstraint.GetArray("m_slaves")
                                        .Select(p => ProcessBoneConstraintSlave(p))
                                        .Where(p => p != null);

            children = slaves.Concat(targets);
        }

        var childrenKV = new KVObject(null, true);
        foreach (var child in children)
        {
            childrenKV.AddProperty(null, MakeValue(child));
        }
        node.AddProperty("children", MakeValue(childrenKV));
    }

    static KVObject ProcessBoneConstraint(KVObject boneConstraint)
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

        //These constraints are stored the same way in the .vmdl and the compiled model
        if (targetClassName == "AnimConstraintPoseSpaceBone" || targetClassName == "AnimConstraintPoseSpaceMorph" || targetClassName == "AnimConstraintDotToMorph")
        {
            foreach (var property in boneConstraint.Properties)
            {
                if (property.Key == "_class")
                {
                    continue;
                }
                node.AddProperty(property.Key, property.Value);
            }
            return node;
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
        var stringTokenKeys = model.Skeleton.Bones.Select(b => b.Name);
        if (RenderMeshesToExtract.Count > 0)
        {
            var mesh = RenderMeshesToExtract.First().Mesh;
            stringTokenKeys = stringTokenKeys.Concat(mesh.Attachments.Keys);
        }

        StringToken.Store(stringTokenKeys);

        var childrenKV = new KVObject(null, true, boneConstraintsList.Length);

        foreach (var boneConstraint in boneConstraintsList)
        {
            var constraint = ProcessBoneConstraint(boneConstraint);
            if (constraint != null)
            {
                childrenKV.AddProperty(null, MakeValue(constraint));
            }
        }

        var constraintListNode = MakeNode("AnimConstraintList",
            ("children", MakeValue(childrenKV))
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

        return angles * (180 / MathF.PI);
    }

    static void AddBonesRecursive(IEnumerable<Bone> bones, KVObject parent)
    {
        foreach (var bone in bones)
        {
            var boneDefinitionNode = MakeNode(
                "Bone",
                ("name", bone.Name),
                ("origin", bone.Position),
                ("angles", ToEulerAngles(bone.Angle)),
                ("do_not_discard", true)
            );

            AddItem(parent, boneDefinitionNode);

            if (bone.Children.Count > 0)
            {
                var childBones = new KVObject(null, isArray: true);
                boneDefinitionNode.AddProperty("children", MakeValue(childBones));
                AddBonesRecursive(bone.Children, childBones);
            }
        }
    }

    public string ToValveModel()
    {
        var kv = new KVObject(null);

        var root = MakeListNode("RootNode");
        kv.AddProperty("rootNode", MakeValue(root.Node));

        Lazy<KVObject> MakeLazyList(string className)
        {
            return new Lazy<KVObject>(() =>
            {
                var list = MakeListNode(className);
                AddItem(root.Children, list.Node);

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

        var boneMarkupList = MakeListNode("BoneMarkupList");
        AddItem(root.Children, boneMarkupList.Node);
        boneMarkupList.Node.AddProperty("bone_cull_type", MakeValue("None"));

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
                    var importFilter = new KVObject("import_filter");
                    {
                        importFilter.AddProperty("exclude_by_default", MakeValue(renderMesh.ImportFilter.ExcludeByDefault));
                        importFilter.AddProperty("exception_list", MakeArrayValue(renderMesh.ImportFilter.Filter));
                    }

                    renderMeshFile.AddProperty("import_filter", MakeValue(importFilter));
                }

                AddItem(renderMeshList.Value, renderMeshFile);
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

                var groupedChoices = new Dictionary<string, List<(string FullName, string ChoiceName, ulong Mask)>>();

                for (var i = 0; i < meshGroups.Length; i++)
                {
                    var fullName = meshGroups[i];
                    var split = fullName.Split("_@");

                    if (split.Length < 2)
                    {
                        continue;
                    }

                    // No mask will show up as 'Empty' in editor
                    var mask = i < meshGroupMasks.Length ? meshGroupMasks[i] : 0UL;

                    var groupName = split[0];
                    var choiceName = split[1];

                    groupedChoices.TryAdd(groupName, []);
                    groupedChoices[groupName].Add((fullName, choiceName, mask));
                }

                foreach (var (groupName, choices) in groupedChoices)
                {
                    var choiceList = new KVObject(null, isArray: true, choices.Count);
                    var bodyGroup = MakeNode("BodyGroup",
                        ("name", groupName),
                        ("children", choiceList)
                    );

                    if (hideInTools.Contains(groupName))
                    {
                        bodyGroup.AddProperty("hidden_in_tools", MakeValue(true));
                    }

                    var i = 0;
                    foreach (var (key, name, mask) in choices)
                    {
                        var meshGroupChoice = MakeNode("BodyGroupChoice");

                        if (name != i.ToString(CultureInfo.InvariantCulture))
                        {
                            meshGroupChoice.AddProperty("name", MakeValue(name));
                        }

                        if (hideInTools.Contains(key))
                        {
                            meshGroupChoice.AddProperty("hide_in_tools", MakeValue(true));
                        }

                        var meshes = new KVObject(null, isArray: true);
                        meshGroupChoice.AddProperty("meshes", MakeValue(meshes));

                        foreach (var renderMesh in RenderMeshesToExtract)
                        {
                            if ((mask >> renderMesh.Index & 1) == 0)
                            {
                                continue;
                            }

                            meshes.AddProperty(null, MakeValue(renderMesh.Name));
                        }

                        AddItem(choiceList, meshGroupChoice);
                        i++;
                    }

                    AddItem(bodyGroupList.Value, bodyGroup);
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
                    ("relative_origin", mainInfluence.Offset),
                    ("relative_angles", ToEulerAngles(mainInfluence.Rotation)),
                    ("weight", mainInfluence.Weight)
                );

                if (attachment.Length > 1)
                {
                    var children = new KVObject(null, true, attachment.Length - 1);
                    for (var i = 0; i < attachment.Length - 1; i++)
                    {
                        var influence = attachment[i];
                        var childNode = MakeNode("AttachmentInfluence",
                            ("parent_bone", influence.Name),
                            ("relative_origin", influence.Offset),
                            ("relative_angles", ToEulerAngles(influence.Rotation)),
                            ("weight", influence.Weight)
                        );
                        children.AddProperty(null, MakeValue(childNode));
                    }
                    node.AddProperty("children", MakeValue(children));
                }

                AddItem(attachmentList.Value, node);
            }
        }

        if (AnimationsToExtract.Count > 0)
        {
            foreach (var animation in AnimationsToExtract)
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
                    animationFile.AddProperty("activity_name", MakeValue(activity.Name));
                    animationFile.AddProperty("activity_weight", MakeValue(activity.Weight));
                }

                var childrenKV = new KVObject(null, true, 1);
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

                    childrenKV.AddProperty(null, MakeValue(extractMotion));
                }
                foreach (var animEvent in animation.Anim.Events)
                {
                    var animEventNode = MakeNode("AnimEvent",
                        ("event_class", animEvent.Name),
                        ("event_frame", animEvent.Frame)
                    );

                    if (animEvent.EventData != null)
                    {
                        animEventNode.AddProperty("event_keys", MakeValue(animEvent.EventData));
                    }
                    childrenKV.AddProperty(null, MakeValue(animEventNode));
                }

                if (childrenKV.Count > 0)
                {
                    animationFile.AddProperty("children", MakeValue(childrenKV));
                }

                AddItem(animationList.Value, animationFile);
            }
        }

        if (PhysHullsToExtract.Count > 0 || PhysMeshesToExtract.Count > 0)
        {
            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                var globalReplace = PhysicsToRenderMaterialNameProvider == null;
                var remapTable = globalReplace ? null
                    : SurfaceTagCombos.ToDictionary(
                        combo => combo.StringMaterial,
                        combo => PhysicsToRenderMaterialNameProvider(combo)
                    );

                RemapMaterials(remapTable, globalReplace);
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

            if (model.Resource.GetBlockByType(BlockType.ASEQ) is KeyValuesOrNTRO sequenceData)
            {
                ExtractSequenceData(sequenceData);
            }

            if (model.Skeleton.Roots.Length > 0)
            {
                AddBonesRecursive(model.Skeleton.Roots, skeleton.Value);
            }
        }

        if (physAggregateData is not null)
        {
            foreach (var physicsPart in physAggregateData.Parts)
            {
                foreach (var sphere in physicsPart.Shape.Spheres)
                {
                    var physicsShapeSphere = MakeNode(
                        "PhysicsShapeSphere",
                        ("surface_prop", PhysicsSurfaceNames[sphere.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[sphere.CollisionAttributeIndex])),
                        ("radius", sphere.Shape.Radius),
                        ("center", sphere.Shape.Center)
                    );

                    AddItem(physicsShapeList.Value, physicsShapeSphere);
                }

                foreach (var capsule in physicsPart.Shape.Capsules)
                {
                    var physicsShapeCapsule = MakeNode(
                        "PhysicsShapeCapsule",
                        ("surface_prop", PhysicsSurfaceNames[capsule.SurfacePropertyIndex]),
                        ("collision_tags", string.Join(" ", PhysicsCollisionTags[capsule.CollisionAttributeIndex])),
                        ("radius", capsule.Shape.Radius),
                        ("point0", capsule.Shape.Center[0]),
                        ("point1", capsule.Shape.Center[1])
                    );

                    AddItem(physicsShapeList.Value, physicsShapeCapsule);
                }
            }
        }

        if (Translation != Vector3.Zero)
        {
            AddItem(modelModifierList.Value, MakeNode("ModelModifier_Translate", ("translation", Translation)));
        }


        return new KV3File(kv, format: "modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca}").ToString();
        //return new KV3File(kv, format: "modeldoc32:version{c5dcef98-b629-46ab-88e3-a17c005c935e}").ToString();

        #region Local Functions
        void HandlePhysMeshNode<TShape>(ShapeDescriptor<TShape> shapeDesc, string fileName)
            where TShape : struct
        {
            var surfacePropName = PhysicsSurfaceNames[shapeDesc.SurfacePropertyIndex];
            var collisionTags = PhysicsCollisionTags[shapeDesc.CollisionAttributeIndex];

            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                AddItem(renderMeshList.Value, MakeNode("RenderMeshFile", ("filename", fileName)));
                return;
            }

            var className = shapeDesc switch
            {
                HullDescriptor => "PhysicsHullFile",
                MeshDescriptor => "PhysicsMeshFile",
                _ => throw new NotImplementedException()
            };

            // TODO: per faceSet surface_prop
            var physicsShapeFile = MakeNode(
                className,
                ("filename", fileName),
                ("surface_prop", surfacePropName),
                ("collision_tags", string.Join(" ", collisionTags)),
                ("name", shapeDesc.UserFriendlyName ?? fileName)
            );

            AddItem(physicsShapeList.Value, physicsShapeFile);
        }

        void RemapMaterials(
            IReadOnlyDictionary<string, string> remapTable = null,
            bool globalReplace = false,
            string globalDefault = "materials/tools/toolsnodraw.vmat")
        {
            var remaps = new KVObject(null, isArray: true);
            AddItem(materialGroupList.Value,
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
                var remap = new KVObject(null);
                remap.AddProperty("from", MakeValue(from));
                remap.AddProperty("to", MakeValue(to));
                AddItem(remaps, remap);
            }
        }

        KVObject GetHitboxNode(Hitbox hitbox)
        {
            var node = hitbox.ShapeType switch
            {
                Hitbox.HitboxShape.Box => MakeNode("Hitbox",
                    ("hitbox_mins", hitbox.MinBounds),
                    ("hitbox_maxs", hitbox.MaxBounds)
                ),
                Hitbox.HitboxShape.Capsule => MakeNode("HitboxCapsule",
                    ("radius", hitbox.ShapeRadius),
                    ("point0", hitbox.MinBounds),
                    ("point1", hitbox.MaxBounds)
                ),
                Hitbox.HitboxShape.Sphere => MakeNode("HitboxSphere",
                    ("center", hitbox.MinBounds),
                    ("radius", hitbox.ShapeRadius)
                ),
                _ => throw new NotImplementedException($"Unknown hitbox shape type: {hitbox.ShapeType}")
            };

            node.AddProperty("name", MakeValue(hitbox.Name));
            node.AddProperty("parent_bone", MakeValue(hitbox.BoneName));
            node.AddProperty("surface_property", MakeValue(hitbox.SurfaceProperty));
            node.AddProperty("translation_only", MakeValue(hitbox.TranslationOnly));
            node.AddProperty("group_id", MakeValue(hitbox.GroupId));

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
                var children = new KVObject(null, true, pair.Value.Length);
                var hitboxSet = MakeNode("HitboxSet", ("name", pair.Key), ("children", children));

                foreach (var hitbox in pair.Value)
                {
                    var hitboxNode = GetHitboxNode(hitbox);
                    children.AddProperty(null, MakeValue(hitboxNode));
                }

                AddItem(hitboxSetList.Value, hitboxSet);
            }
        }

        void ExtractSequenceData(KeyValuesOrNTRO sequenceData)
        {
            var boneMasks = sequenceData.Data.GetArray<KVObject>("m_localBoneMaskArray");
            var boneNames = sequenceData.Data.GetArray<string>("m_localBoneNameArray");

            foreach (var boneMask in boneMasks)
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

                var weights = new KVObject(null, isArray: true, boneArray.Length);
                var weightListNode = MakeNode("WeightList",
                    ("name", name),
                    ("weights", weights)
                );

                foreach (var (boneIndex, boneWeight) in boneArray.Zip(boneWeights))
                {
                    var weightDefinition = new KVObject(null, 2);
                    var boneName = boneNames[boneIndex];

                    weightDefinition.AddProperty("bone", MakeValue(boneName));
                    weightDefinition.AddProperty("weight", MakeValue(boneWeight));
                    AddItem(weights, weightDefinition);
                }

                AddItem(weightLists.Value, weightListNode);
            }
        }

        void ExtractModelKeyValues(KVObject rootNode)
        {
            if (model.Data.ContainsKey("m_refAnimIncludeModels"))
            {
                foreach (var animIncludeModel in model.Data.GetArray<string>("m_refAnimIncludeModels"))
                {
                    AddItem(animationList.Value, MakeNode("AnimIncludeModel", ("model", animIncludeModel)));
                }
            }

            var breakPieceList = MakeLazyList("BreakPieceList");
            var gameDataList = MakeLazyList("GameDataList");

            var keyvaluesString = model.Data.GetSubCollection("m_modelInfo").GetProperty<string>("m_keyValueText");

            const int NullKeyValuesLengthLimit = 140;
            if (string.IsNullOrEmpty(keyvaluesString)
            || !keyvaluesString.StartsWith("<!-- kv3 ", StringComparison.Ordinal)
            || keyvaluesString.Length < NullKeyValuesLengthLimit)
            {
                return;
            }

            KVObject keyvalues;
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(keyvaluesString)))
            {
                try
                {
                    keyvalues = KeyValues3.ParseKVFile(ms).Root;
                }
                catch (Exception e)
                {
                    // TODO: Current parser fails when root is "null", so just skip over them for now
                    Console.Error.WriteLine(e.ToString());
                    return;
                }
            }

            if (keyvalues.ContainsKey("anim_graph_resource"))
            {
                rootNode.AddProperty("anim_graph_name", MakeValue(keyvalues.GetProperty<string>("anim_graph_resource")));
            }

            if (keyvalues.ContainsKey("BoneConstraintList"))
            {
                var boneConstraintListData = keyvalues.GetArray("BoneConstraintList");
                var boneConstraintList = ExtractBoneConstraints(boneConstraintListData);
                root.Children.AddProperty(null, MakeValue(boneConstraintList));
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
                    var genericDataList = keyvalues.GetArray<KVObject>(dataKey);
                    foreach (var genericData in genericDataList)
                    {
                        AddGenericGameData(gameDataList.Value, genericDataClass.Class, genericData);
                    }
                }
            }

            if (keyvalues.ContainsKey("LookAtList"))
            {
                var lookAtList = keyvalues.GetSubCollection("LookAtList");
                foreach (var item in lookAtList)
                {
                    var lookAtChain = item.Value as KVObject;
                    AddGenericGameData(gameDataList.Value, "LookAtChain", lookAtChain, "lookat_chain");
                }
            }

            if (keyvalues.ContainsKey("MovementSettings"))
            {
                var movementSettings = keyvalues.GetProperty<KVObject>("MovementSettings");
                AddGenericGameData(gameDataList.Value, "MovementSettings", movementSettings, "movementsettings");
            }


            if (keyvalues.ContainsKey("break_list"))
            {
                foreach (var breakPiece in keyvalues.GetArray<KVObject>("break_list"))
                {
                    var breakPieceFile = MakeNode("BreakPieceExternal");
                    foreach (var property in breakPiece.Properties)
                    {
                        var (key, value) = (property.Key, property.Value);
                        // Remove resource flag from value
                        if (value is KVFlaggedValue)
                        {
                            value = MakeValue(value.Value);
                        }
                        breakPieceFile.AddProperty(key, value);
                    }
                    AddItem(breakPieceList.Value, breakPieceFile);
                }
            }

            static void AddGenericGameData(KVObject gameDataList, string genericDataClass, KVObject genericData, string dataKey = null)
            {
                // Remove quotes from keys
                genericData.Properties.Keys.ToList().ForEach(k =>
                {
                    var trimmed = k.Trim('"');
                    if (trimmed != k)
                    {
                        genericData.Properties[trimmed] = genericData.Properties[k];
                        genericData.Properties.Remove(k);
                    }
                });

                var name = "";
                if (genericData.ContainsKey("name"))
                {
                    name = genericData.GetStringProperty("name");
                }

                KVObject genericGameData;
                if (dataKey == null)
                {
                    genericGameData = MakeNode("GenericGameData",
                        ("name", name),
                        ("game_class", genericDataClass),
                        ("game_keys", genericData)
                    );
                }
                else
                {
                    genericGameData = MakeNode(genericDataClass,
                        ("name", name),
                        (dataKey, genericData)
                    );
                }

                AddItem(gameDataList, genericGameData);
            }
        }
        #endregion
    }
}
