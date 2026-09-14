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
    private HashSet<string>? markedUpPhysicsBodies;

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
    /// Adds one body markup for every body in <see cref="GetMarkedUpPhysicsBodies"/>, carrying the tag of the
    /// <c>CPhysicsBodyGameMarkupData</c> entry that targets that body.
    /// </summary>
    /// <remarks>
    /// A body markup compiles into both the part fields and that game data entry, so
    /// <see cref="AddPhysicsBodyGameData"/> writes only the entries no markup covers. The compiler merges those with
    /// the markup only while the game data list comes first in the document; with the markup list first it drops the
    /// whole game data passthrough.
    /// </remarks>
    private void AddPhysicsBodyMarkup(ModelDocLists lists, PhysAggregateData physics)
    {
        var markedUpBodies = GetMarkedUpPhysicsBodies();
        var gameMarkups = GetPhysicsBodyGameMarkups();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partsData = physics.Data.GetArray("m_parts");

        for (var i = 0; i < physics.Parts.Length; i++)
        {
            var parentBone = physics.GetParentBoneName(i);

            if (markedUpBodies.Contains(parentBone) && added.Add(parentBone))
            {
                gameMarkups.TryGetValue(parentBone, out var gameMarkup);
                lists.PhysicsBodyMarkup.Add(BuildPhysicsBodyMarkup(partsData[i], parentBone, gameMarkup));
            }
        }
    }

    /// <summary>
    /// Gets the bodies that get a body markup: every named body when a joint exports, and otherwise every named body
    /// whose part fields differ from what the compiler writes for a body without markup.
    /// </summary>
    private HashSet<string> GetMarkedUpPhysicsBodies()
    {
        if (markedUpPhysicsBodies != null)
        {
            return markedUpPhysicsBodies;
        }

        var bodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (physAggregateData is { } physics)
        {
            var hasJoints = false;

            foreach (var joint in physics.Joints)
            {
                if (BuildPhysicsJoint(physics, joint) is not null)
                {
                    hasJoints = true;
                    break;
                }
            }

            var partsData = physics.Data.GetArray("m_parts");

            for (var i = 0; i < physics.Parts.Length; i++)
            {
                var parentBone = physics.GetParentBoneName(i);

                if (parentBone.Length > 0 && (hasJoints || IsPartAuthored(physics.Parts[i], partsData[i])))
                {
                    bodies.Add(parentBone);
                }
            }
        }

        return markedUpPhysicsBodies = bodies;
    }

    /// <summary>
    /// Gets the <c>CPhysicsBodyGameMarkupData</c> entries that carry a tag, keyed by the body they target.
    /// </summary>
    private Dictionary<string, KVObject> GetPhysicsBodyGameMarkups()
    {
        var gameMarkups = new Dictionary<string, KVObject>(StringComparer.OrdinalIgnoreCase);
        var markups = model?.KeyValues.GetSubCollection("CPhysicsBodyGameMarkupData")?.GetSubCollection("m_PhysicsBodyMarkupByBoneName");

        if (markups != null)
        {
            foreach (var (name, markup) in markups)
            {
                if (markup.ContainsKey("m_Tag"))
                {
                    gameMarkups[markup.GetStringProperty("m_TargetBody", name!)] = markup;
                }
            }
        }

        return gameMarkups;
    }

    /// <summary>
    /// Writes the <c>CPhysicsBodyGameMarkupData</c> game data no body markup compiles into: the entries targeting a
    /// body outside <paramref name="markedUpBodies"/>, and every other key.
    /// </summary>
    private static void AddPhysicsBodyGameData(ModelDocLists lists, KVObject? gameData, HashSet<string> markedUpBodies)
    {
        if (gameData is null)
        {
            return;
        }

        var remaining = KVObject.Collection();

        foreach (var (key, value) in gameData)
        {
            if (key != "m_PhysicsBodyMarkupByBoneName")
            {
                remaining.Add(key!, value);
                continue;
            }

            var unexported = KVObject.Collection();

            foreach (var (name, markup) in value)
            {
                if (!markedUpBodies.Contains(markup.GetStringProperty("m_TargetBody", name!)))
                {
                    unexported.Add(name!, markup);
                }
            }

            if (unexported.Count > 0)
            {
                remaining.Add(key, unexported);
            }
        }

        if (remaining.Count > 0)
        {
            AddGenericGameData(lists.GameData, "CPhysicsBodyGameMarkupData", remaining);
        }
    }

    private static bool IsPartAuthored(Part part, KVObject partData)
        => part.Mass != 0f
            || part.InertiaScale != 1f
            || part.LinearDamping != 0f
            || part.AngularDamping != 0f
            || partData.GetFloatProperty("m_flLinearDrag", 1f) != 1f
            || partData.GetFloatProperty("m_flAngularDrag", 1f) != 1f
            || part.OverrideMassCenter
            || part.MassCenterOverride != Vector3.Zero
            || HasContinuousCollisionDisabled(partData);

    /// <summary>
    /// Gets whether a part disables continuous collision detection. Only parts that carry drag fields have the markup
    /// key for it.
    /// </summary>
    private static bool HasContinuousCollisionDisabled(KVObject partData)
        => partData.ContainsKey("m_flLinearDrag") && (partData.GetInt32Property("m_nFlags") & 0x20) != 0;

    private static KVObject BuildPhysicsBodyMarkup(KVObject part, string bodyName, KVObject? gameMarkup)
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

        if (HasContinuousCollisionDisabled(part))
        {
            node.Add("disable_continuous_collision_detection", true);
        }

        if (gameMarkup != null)
        {
            AddIfPresent(node, "tag", gameMarkup, "m_Tag");
        }

        return node;
    }
}
