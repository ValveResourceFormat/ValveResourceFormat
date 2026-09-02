using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes for the collision model: physics joints and the markup carried by the
/// shapes they connect.
/// </summary>
partial class ModelExtract
{
    /// <summary>
    /// Builds a PhysicsJointList child node for one joint, or <see langword="null"/> for a joint type
    /// with no known ModelDoc node class. The limit attributes are recovered per joint type: only
    /// <see cref="JointType.Conical"/>, <see cref="JointType.Revolute"/> and
    /// <see cref="JointType.Prismatic"/> have confirmed motion-limit authoring keys.
    /// </summary>
    static KVObject? BuildPhysicsJoint(PhysAggregateData physAggregateData, Joint joint)
    {
        var className = joint.Type switch
        {
            JointType.Null => "PhysicsJointNull",
            JointType.Spherical => "PhysicsJointSpherical",
            JointType.Prismatic => "PhysicsJointPrismatic",
            JointType.Revolute => "PhysicsJointRevolute",
            JointType.Conical => "PhysicsJointConical",
            JointType.Weld => "PhysicsJointWeld",
            JointType.Wheel => "PhysicsJointWheel",
            _ => null,
        };

        if (className is null)
        {
            return null;
        }

        var jointNode = MakeNode(
            className,
            ("parent_body", physAggregateData.GetParentBoneName(joint.Body1)),
            ("child_body", physAggregateData.GetParentBoneName(joint.Body2)),
            ("anchor_origin", ToKVArray(joint.Frame1.Position)),
            ("anchor_angles", ToKVArray(EntityTransformHelper.ToEulerAngles(joint.Frame1.Rotation))),
            ("collision_enabled", joint.EnableCollision),
            ("friction", joint.Friction)
        );

        switch (joint.Type)
        {
            case JointType.Conical:
                jointNode.Add("enable_swing_limit", joint.EnableSwingLimit);
                jointNode.Add("swing_limit", float.RadiansToDegrees(joint.SwingLimit.Max));
                jointNode.Add("enable_twist_limit", joint.EnableTwistLimit);
                jointNode.Add("min_twist_angle", float.RadiansToDegrees(joint.TwistLimit.Min));
                jointNode.Add("max_twist_angle", float.RadiansToDegrees(joint.TwistLimit.Max));
                break;
            case JointType.Revolute:
                jointNode.Add("enable_limit", joint.EnableTwistLimit);
                jointNode.Add("min_angle", float.RadiansToDegrees(joint.TwistLimit.Min));
                jointNode.Add("max_angle", float.RadiansToDegrees(joint.TwistLimit.Max));
                break;
            case JointType.Prismatic:
                jointNode.Add("enable_limit", joint.EnableLinearLimit);
                jointNode.Add("min_offset", joint.LinearLimit.Min);
                jointNode.Add("max_offset", joint.LinearLimit.Max);
                break;
        }

        return jointNode;
    }

    /// <summary>
    /// Writes the hit group a physics shape belongs to. Shipped content leaves this at the invalid
    /// placeholder, which the compiler does not write back, so only a real group is emitted.
    /// </summary>
    static void AddHitGroup<TShape>(KVObject node, ShapeDescriptor<TShape> shape) where TShape : struct
    {
        if (!string.IsNullOrEmpty(shape.HitGroupName) && shape.HitGroupName != "HITGROUP_INVALID")
        {
            node.Add("hitgroupname", shape.HitGroupName);
        }
    }

    private void AddPhysicsShapeFileNodes(ModelDocLists lists)
    {
        if (PhysHullsToExtract.Count > 0 || PhysMeshesToExtract.Count > 0)
        {
            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                if (PhysicsToRenderMaterialNameProvider is null)
                {
                    RemapMaterials(lists, globalReplace: true);
                }
                else
                {
                    var remapTable = SurfaceTagCombos.ToDictionary(
                        combo => combo.StringMaterial,
                        combo => PhysicsToRenderMaterialNameProvider(combo)
                    );
                    RemapMaterials(lists, remapTable, globalReplace: false);
                }
            }

            foreach (var (physHull, fileName, parentBone, _) in PhysHullsToExtract)
            {
                AddPhysMeshNode(lists, physHull, fileName, parentBone);
            }

            foreach (var (physMesh, fileName, parentBone, _) in PhysMeshesToExtract)
            {
                AddPhysMeshNode(lists, physMesh, fileName, parentBone);
            }
        }
    }

    private void AddPhysicsBodyNodes(ModelDocLists lists)
    {
        if (physAggregateData is not null)
        {
            // Bones that already carry body markup as game data round-trip their mass through it, and the
            // compiler rejects a second markup for the same body. The lookup is case-insensitive because
            // resourcecompiler matches target_body to the existing markup's bone name that way.
            var existingMarkupBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var physicsBodyMarkupData = model?.KeyValues.GetSubCollection("CPhysicsBodyGameMarkupData");
            var physicsBodyMarkupByBoneName = physicsBodyMarkupData?.GetSubCollection("m_PhysicsBodyMarkupByBoneName");

            if (physicsBodyMarkupByBoneName != null)
            {
                foreach (var (boneName, _) in physicsBodyMarkupByBoneName)
                {
                    existingMarkupBones.Add(boneName);
                }
            }

            for (var i = 0; i < physAggregateData.Parts.Length; i++)
            {
                var physicsPart = physAggregateData.Parts[i];
                var parentBone = physAggregateData.GetParentBoneName(i);

                var hasOverrides = physicsPart.Mass != 0f
                    || physicsPart.InertiaScale != 1f
                    || physicsPart.LinearDamping != 0f
                    || physicsPart.AngularDamping != 0f
                    || physicsPart.OverrideMassCenter;

                if (hasOverrides && !existingMarkupBones.Contains(parentBone))
                {
                    var bodyMarkup = MakeNode("PhysicsBodyMarkup", ("target_body", parentBone));

                    if (physicsPart.Mass != 0f)
                    {
                        bodyMarkup.Add("mass_override", physicsPart.Mass);
                    }

                    if (physicsPart.InertiaScale != 1f)
                    {
                        bodyMarkup.Add("inertia_scale", physicsPart.InertiaScale);
                    }

                    if (physicsPart.LinearDamping != 0f)
                    {
                        bodyMarkup.Add("linear_damping", physicsPart.LinearDamping);
                    }

                    if (physicsPart.AngularDamping != 0f)
                    {
                        bodyMarkup.Add("angular_damping", physicsPart.AngularDamping);
                    }

                    if (physicsPart.OverrideMassCenter)
                    {
                        bodyMarkup.Add("use_mass_center_override", true);
                        bodyMarkup.Add("mass_center_override", ToKVArray(physicsPart.MassCenterOverride));
                    }

                    lists.PhysicsBodyMarkup.Add(bodyMarkup);
                }

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

                    AddHitGroup(physicsShapeSphere, sphere);

                    lists.PhysicsShapes.Add(physicsShapeSphere);
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

                    AddHitGroup(physicsShapeCapsule, capsule);

                    lists.PhysicsShapes.Add(physicsShapeCapsule);
                }
            }

            foreach (var joint in physAggregateData.Joints)
            {
                var jointNode = BuildPhysicsJoint(physAggregateData, joint);

                if (jointNode is not null)
                {
                    lists.PhysicsJoints.Add(jointNode);
                }
            }
        }
    }

    private void AddPhysMeshNode<TShape>(ModelDocLists lists, ShapeDescriptor<TShape> shapeDesc, string fileName, string parentBone)
        where TShape : struct
    {
        var surfacePropName = PhysicsSurfaceNames[shapeDesc.SurfacePropertyIndex];
        var collisionTags = PhysicsCollisionTags[shapeDesc.CollisionAttributeIndex];

        if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
        {
            lists.RenderMeshes.Add(MakeNode("RenderMeshFile", ("filename", fileName)));
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
            ("parent_bone", parentBone),
            ("surface_prop", surfacePropName),
            ("collision_tags", string.Join(" ", collisionTags)),
            ("name", shapeName)
        );

        AddHitGroup(physicsShapeFile, shapeDesc);

        lists.PhysicsShapes.Add(physicsShapeFile);
    }

    private static void RemapMaterials(ModelDocLists lists,
        IReadOnlyDictionary<string, string>? remapTable = null,
        bool globalReplace = false,
        string globalDefault = "materials/tools/toolsnodraw.vmat")
    {
        var remaps = KVObject.Array();
        lists.MaterialGroups.Add(
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
}
