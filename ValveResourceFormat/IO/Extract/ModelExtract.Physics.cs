using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    private void ExtractPhysicsJoints(KVObject rootChildren)
    {
        var joints = physAggregateData!.Data.GetArray("m_joints");
        if (joints == null || joints.Count == 0)
        {
            return;
        }

        var jointList = MakeListNode("PhysicsJointList");
        foreach (var joint in joints)
        {
            var node = BuildPhysicsJoint(physAggregateData, joint);
            if (node != null)
            {
                jointList.Children.Add(node);
            }
            else
            {
                ProgressReporter?.Report($"Unable to export physics joint type {joint.GetInt32Property("m_nType")} between bodies {joint.GetInt32Property("m_nBody1")} and {joint.GetInt32Property("m_nBody2")}.");
            }
        }

        if (jointList.Children.Count == 0)
        {
            return;
        }

        rootChildren.Add(jointList.Node);

        // Geometry alone does not preserve the mass and damping that make the articulated bodies stable.
        var markupList = MakeListNode("PhysicsBodyMarkupList");
        var parts = physAggregateData.Data.GetArray("m_parts");
        for (var i = 0; i < parts.Count; i++)
        {
            var bodyName = physAggregateData.GetParentBoneName(i);
            if (!string.IsNullOrEmpty(bodyName))
            {
                markupList.Children.Add(BuildPhysicsBodyMarkup(parts[i], bodyName));
            }
        }

        if (markupList.Children.Count > 0)
        {
            MergePhysicsBodyGameMarkup(rootChildren, markupList.Children);
            rootChildren.Add(markupList.Node);
        }
    }

    internal static void MergePhysicsBodyGameMarkup(KVObject rootChildren, KVObject bodyMarkups)
    {
        var bodies = new Dictionary<string, KVObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, body) in bodyMarkups)
        {
            bodies.Add(body.GetStringProperty("target_body"), body);
        }

        foreach (var (_, list) in rootChildren)
        {
            if (list.GetStringProperty("_class") != "GameDataList")
            {
                continue;
            }

            var children = KVObject.Array();
            foreach (var entry in list.GetArray("children"))
            {
                if (entry.GetStringProperty("_class") == "GenericGameData"
                    && entry.GetStringProperty("game_class") == "CPhysicsBodyGameMarkupData")
                {
                    var keys = entry.GetSubCollection("game_keys");
                    var markups = keys.GetSubCollection("m_PhysicsBodyMarkupByBoneName");
                    if (markups != null)
                    {
                        var remaining = KVObject.Collection();
                        foreach (var (name, markup) in markups)
                        {
                            var target = markup.GetStringProperty("m_TargetBody", name!);
                            if (bodies.TryGetValue(target, out var body))
                            {
                                AddIfPresent(body, "tag", markup, "m_Tag");
                            }
                            else
                            {
                                remaining.Add(name!, markup);
                            }
                        }

                        // Body markup nodes regenerate this game data. Keeping both declarations
                        // would make compilation fail with duplicate target bodies.
                        keys["m_PhysicsBodyMarkupByBoneName"] = remaining;
                        if (remaining.Count == 0 && keys.Count == 1)
                        {
                            continue;
                        }
                    }
                }

                children.Add(entry);
            }

            list["children"] = children;
        }
    }

    internal static KVObject? BuildPhysicsJoint(PhysAggregateData physics, KVObject joint)
    {
        var type = joint.GetInt32Property("m_nType");
        var className = type switch
        {
            3 => "PhysicsJointRevolute",
            4 => "PhysicsJointConical",
            _ => null,
        };

        if (className == null)
        {
            return null;
        }

        var parentIndex = joint.GetInt32Property("m_nBody1", -1);
        var childIndex = joint.GetInt32Property("m_nBody2", -1);
        var parentName = physics.GetParentBoneName(parentIndex);
        var childName = physics.GetParentBoneName(childIndex);
        if (string.IsNullOrEmpty(parentName) || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        var frame1 = joint.GetSubCollection("m_Frame1").ToTransform();
        var frame2 = joint.GetSubCollection("m_Frame2").ToTransform();
        var node = MakeNode(className,
            ("parent_body", parentName),
            ("child_body", childName),
            ("anchor_origin", ToKVArray(frame1.Position)),
            ("anchor_angles", ToKVArray(EntityTransformHelper.ToEulerAngles(Quaternion.Normalize(frame1.Rotation)))));

        AddIfPresent(node, "collision_enabled", joint, "m_bEnableCollision");
        if (joint.ContainsKey("m_nFlags"))
        {
            node.Add("use_block_solver", (joint.GetInt32Property("m_nFlags") & 2) != 0);
        }

        if (joint.GetBooleanProperty("m_bIsAngularConstraintDisabled"))
        {
            node.Add("constraint_space", "linear_only");
        }
        else if (joint.GetBooleanProperty("m_bIsLinearConstraintDisabled"))
        {
            node.Add("constraint_space", "angular_only");
        }

        var twistLimit = joint.GetSubCollection("m_TwistLimit");
        if (type == 4)
        {
            AddIfPresent(node, "enable_swing_limit", joint, "m_bEnableSwingLimit");
            node.Add("swing_limit", float.RadiansToDegrees(joint.GetSubCollection("m_SwingLimit").GetFloatProperty("m_flMax")));
            AddIfPresent(node, "enable_twist_limit", joint, "m_bEnableTwistLimit");
            node.Add("min_twist_angle", float.RadiansToDegrees(twistLimit.GetFloatProperty("m_flMin")));
            node.Add("max_twist_angle", float.RadiansToDegrees(twistLimit.GetFloatProperty("m_flMax")));

            var bindPose = physics.BindPose;
            if (parentIndex < bindPose.Length && childIndex < bindPose.Length)
            {
                var parentRotation = Quaternion.CreateFromRotationMatrix(bindPose[parentIndex]);
                var childRotation = Quaternion.CreateFromRotationMatrix(bindPose[childIndex]);
                var parentFrame = Quaternion.Normalize(parentRotation * frame1.Rotation);
                var childFrame = Quaternion.Normalize(childRotation * frame2.Rotation);

                // ModelDoc applies the negated offset angles in the parent joint frame. Negating
                // the Euler angles is not equivalent to inverting their quaternion.
                var offset = -EntityTransformHelper.ToEulerAngles(Quaternion.Inverse(parentFrame) * childFrame);
                node.Add("swing_offset_angle", ToKVArray(offset));
            }
        }
        else
        {
            AddIfPresent(node, "enable_limit", joint, "m_bEnableTwistLimit");
            node.Add("min_angle", float.RadiansToDegrees(twistLimit.GetFloatProperty("m_flMin")));
            node.Add("max_angle", float.RadiansToDegrees(twistLimit.GetFloatProperty("m_flMax")));
        }

        var friction = joint.GetFloatProperty("m_flFriction");
        var elasticity = joint.GetFloatProperty("m_flElasticity");
        var plasticity = joint.GetFloatProperty("m_flPlasticity");
        node.Add("motion_resistance", plasticity != 0 ? "plastic" : elasticity != 0 ? "elastic" : friction != 0 ? "friction" : "none");
        AddIfPresent(node, "friction", joint, "m_flFriction");
        AddIfPresent(node, "elasticity", joint, "m_flElasticity");
        AddIfPresent(node, "elastic_damping", joint, "m_flElasticDamping");
        AddIfPresent(node, "plasticity", joint, "m_flPlasticity");

        return node;
    }

    internal static KVObject BuildPhysicsBodyMarkup(KVObject part, string bodyName)
    {
        var node = MakeNode("PhysicsBodyMarkup", ("target_body", bodyName));
        AddIfPresent(node, "mass_override", part, "m_flMass");
        AddIfPresent(node, "inertia_scale", part, "m_flInertiaScale");
        AddIfPresent(node, "linear_damping", part, "m_flLinearDamping");
        AddIfPresent(node, "angular_damping", part, "m_flAngularDamping");
        AddIfPresent(node, "linear_drag", part, "m_flLinearDrag");
        AddIfPresent(node, "angular_drag", part, "m_flAngularDrag");
        AddIfPresent(node, "use_mass_center_override", part, "m_bOverrideMassCenter");
        AddIfPresent(node, "mass_center_override", part, "m_vMassCenterOverride");
        return node;
    }
}
