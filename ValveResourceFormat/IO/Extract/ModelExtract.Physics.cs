using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    private void ExtractPhysicsJoints(KVObject rootChildren)
    {
        var joints = physAggregateData!.Joints;
        if (joints.Length == 0)
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
                ProgressReporter?.Report($"Unable to export physics joint type {joint.Type} between bodies {joint.Body1} and {joint.Body2}.");
            }
        }

        if (jointList.Children.Count == 0)
        {
            return;
        }

        rootChildren.Add(jointList.Node);

        // Geometry alone does not preserve the mass and damping that make the articulated bodies stable.
        var existingMarkupBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingMarkups = model?.KeyValues.GetSubCollection("CPhysicsBodyGameMarkupData")?.GetSubCollection("m_PhysicsBodyMarkupByBoneName");
        if (existingMarkups != null)
        {
            foreach (var (boneName, _) in existingMarkups)
            {
                existingMarkupBones.Add(boneName!);
            }
        }

        var markupList = MakeListNode("PhysicsBodyMarkupList");
        var parts = physAggregateData.Data.GetArray("m_parts");
        for (var i = 0; i < parts.Count; i++)
        {
            var bodyName = physAggregateData.GetParentBoneName(i);
            if (!string.IsNullOrEmpty(bodyName) && existingMarkupBones.Add(bodyName))
            {
                markupList.Children.Add(BuildPhysicsBodyMarkup(parts[i], bodyName));
            }
        }

        if (markupList.Children.Count > 0)
        {
            rootChildren.Add(markupList.Node);
        }
    }

    internal static KVObject? BuildPhysicsJoint(PhysAggregateData physics, Joint joint)
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

        if (className == null)
        {
            return null;
        }

        var parentName = physics.GetParentBoneName(joint.Body1);
        var childName = physics.GetParentBoneName(joint.Body2);
        if (string.IsNullOrEmpty(parentName) || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        var node = MakeNode(className,
            ("parent_body", parentName),
            ("child_body", childName),
            ("anchor_origin", ToKVArray(joint.Frame1.Position)),
            ("anchor_angles", ToKVArray(EntityTransformHelper.ToEulerAngles(Quaternion.Normalize(joint.Frame1.Rotation)))),
            ("collision_enabled", joint.EnableCollision),
            ("use_block_solver", joint.Flags.HasFlag(JointFlags.UseBlockSolver)));

        if (joint.IsAngularConstraintDisabled)
        {
            node.Add("constraint_space", "linear_only");
        }
        else if (joint.IsLinearConstraintDisabled)
        {
            node.Add("constraint_space", "angular_only");
        }

        switch (joint.Type)
        {
            case JointType.Conical:
                node.Add("enable_swing_limit", joint.EnableSwingLimit);
                node.Add("swing_limit", float.RadiansToDegrees(joint.SwingLimit.Max));
                node.Add("enable_twist_limit", joint.EnableTwistLimit);
                node.Add("min_twist_angle", float.RadiansToDegrees(joint.TwistLimit.Min));
                node.Add("max_twist_angle", float.RadiansToDegrees(joint.TwistLimit.Max));

                var bindPose = physics.BindPose;
                if (joint.Body1 < bindPose.Length && joint.Body2 < bindPose.Length)
                {
                    var parentRotation = Quaternion.CreateFromRotationMatrix(bindPose[joint.Body1]);
                    var childRotation = Quaternion.CreateFromRotationMatrix(bindPose[joint.Body2]);
                    var parentFrame = Quaternion.Normalize(parentRotation * joint.Frame1.Rotation);
                    var childFrame = Quaternion.Normalize(childRotation * joint.Frame2.Rotation);

                    // ModelDoc applies the negated offset angles in the parent joint frame. Negating
                    // the Euler angles is not equivalent to inverting their quaternion.
                    var offset = -EntityTransformHelper.ToEulerAngles(Quaternion.Inverse(parentFrame) * childFrame);
                    node.Add("swing_offset_angle", ToKVArray(offset));
                }

                break;
            case JointType.Revolute:
                node.Add("enable_limit", joint.EnableTwistLimit);
                node.Add("min_angle", float.RadiansToDegrees(joint.TwistLimit.Min));
                node.Add("max_angle", float.RadiansToDegrees(joint.TwistLimit.Max));
                break;
            case JointType.Prismatic:
                node.Add("enable_limit", joint.EnableLinearLimit);
                node.Add("min_offset", joint.LinearLimit.Min);
                node.Add("max_offset", joint.LinearLimit.Max);
                break;
        }

        node.Add("motion_resistance", joint.Plasticity != 0 ? "plastic" : joint.Elasticity != 0 ? "elastic" : joint.Friction != 0 ? "friction" : "none");
        node.Add("friction", joint.Friction);
        node.Add("elasticity", joint.Elasticity);
        node.Add("elastic_damping", joint.ElasticDamping);
        node.Add("plasticity", joint.Plasticity);

        return node;
    }

    private static KVObject BuildPhysicsBodyMarkup(KVObject part, string bodyName)
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
