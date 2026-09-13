using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes for the physics joints between bodies and the markup those bodies carry.
/// </summary>
partial class ModelExtract
{
    private List<KVObject> BuildPhysicsJointNodes(PhysAggregateData physics)
    {
        var jointNodes = new List<KVObject>();

        foreach (var joint in physics.Joints)
        {
            var jointNode = BuildPhysicsJoint(physics, joint);

            if (jointNode is not null)
            {
                jointNodes.Add(jointNode);
            }
            else
            {
                ProgressReporter?.Report($"Unable to export physics joint type {joint.Type} between bodies {joint.Body1} and {joint.Body2}.");
            }
        }

        return jointNodes;
    }

    /// <summary>
    /// Builds a PhysicsJointList child node for one joint, or <see langword="null"/> for a joint type
    /// with no ModelDoc node class or a joint whose bodies have no bone name. Motion limits are written for
    /// <see cref="JointType.Conical"/>, <see cref="JointType.Revolute"/> and <see cref="JointType.Prismatic"/> only.
    /// </summary>
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

    /// <summary>
    /// Adds body markup for every body when the model has joints, and otherwise only for a body that
    /// overrides a default.
    /// </summary>
    private void AddPhysicsBodyMarkup(ModelDocLists lists, PhysAggregateData physics, bool hasJoints)
    {
        // A bone that already carries body markup as game data round-trips its mass through it. The
        // compiler matches target_body to that markup's bone name case-insensitively.
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

        var partsData = physics.Data.GetArray("m_parts");

        for (var i = 0; i < physics.Parts.Length; i++)
        {
            var physicsPart = physics.Parts[i];
            var parentBone = physics.GetParentBoneName(i);

            var needsMarkup = hasJoints
                || physicsPart.Mass != 0f
                || physicsPart.InertiaScale != 1f
                || physicsPart.LinearDamping != 0f
                || physicsPart.AngularDamping != 0f
                || physicsPart.OverrideMassCenter;

            // Markup addresses a body by its bone name.
            if (needsMarkup && parentBone.Length > 0 && existingMarkupBones.Add(parentBone))
            {
                lists.PhysicsBodyMarkup.Add(BuildPhysicsBodyMarkup(partsData[i], parentBone));
            }
        }
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
