using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes for the physics joints between bodies and the markup those bodies carry.
/// </summary>
partial class ModelExtract
{
    private const double CubicInchesToCubicMeters = 0.0254 * 0.0254 * 0.0254;

    private const double UnknownSurfaceKilogramsPerCubicInch = 1d / 64;

    private const double FrictionMotorForcePerKilogram = 360d;

    private HashSet<string>? markedUpPhysicsBodies;

    private List<KVObject> BuildPhysicsJointNodes(PhysAggregateData physics)
    {
        var jointNodes = new List<KVObject>();
        var hasFrictionMotors = physics.Joints.Any(HasFrictionMotors);
        var surfaceProperties = hasFrictionMotors ? LoadSurfaceProperties() : null;

        if (hasFrictionMotors && surfaceProperties == null)
        {
            ProgressReporter?.Report("Unable to load surface properties, physics joint friction is not exported.");
        }

        foreach (var joint in physics.Joints)
        {
            var jointNode = BuildPhysicsJoint(physics, joint, surfaceProperties);

            if (jointNode is null)
            {
                ProgressReporter?.Report($"Unable to export physics joint type {joint.Type} between bodies {joint.Body1} and {joint.Body2}.");
                continue;
            }

            if (HasUnexportedMotors(joint))
            {
                ProgressReporter?.Report($"Unable to export the motors of physics joint type {joint.Type} between bodies {joint.Body1} and {joint.Body2}.");
            }

            jointNodes.Add(jointNode);
        }

        return jointNodes;
    }

    /// <summary>
    /// Builds a PhysicsJointList child node for one joint, or <see langword="null"/> for a joint type with no ModelDoc
    /// node class or a joint whose bodies have no bone name.
    /// </summary>
    /// <remarks>
    /// A record without <see cref="Joint.HasFriction"/> gets only the keys its compiler reads, with friction recovered
    /// from the motors using <paramref name="surfaceProperties"/>, keyed by surface property hash.
    /// </remarks>
    internal static KVObject? BuildPhysicsJoint(PhysAggregateData physics, Joint joint,
        IReadOnlyDictionary<uint, SurfacePhysics>? surfaceProperties = null)
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

        var fixedParent = joint.Flags.HasFlag(JointFlags.Body1Fixed);
        var parentName = fixedParent ? "!fixed" : physics.GetParentBoneName(joint.Body1);
        var childName = physics.GetParentBoneName(joint.Body2);
        if (string.IsNullOrEmpty(parentName) || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        var legacy = !joint.HasFriction;
        var bindPose = physics.BindPose;
        var hasBodyRotations = joint.Body2 < bindPose.Length && (fixedParent || joint.Body1 < bindPose.Length);
        var parentRotation = fixedParent || !hasBodyRotations ? Quaternion.Identity : GetRotation(bindPose[joint.Body1]);
        var childRotation = hasBodyRotations ? GetRotation(bindPose[joint.Body2]) : Quaternion.Identity;

        var anchorRotation = Quaternion.Normalize(joint.Frame1.Rotation);
        var twistMin = joint.TwistLimit.Min;
        var twistMax = joint.TwistLimit.Max;

        if (legacy && !fixedParent && hasBodyRotations && joint.Type == JointType.Revolute && joint.EnableTwistLimit && twistMin == -twistMax)
        {
            anchorRotation = Quaternion.Normalize(Quaternion.Inverse(parentRotation) * childRotation * joint.Frame2.Rotation);
            var recentering = Quaternion.Normalize(Quaternion.Inverse(anchorRotation) * joint.Frame1.Rotation);
            var middle = float.Ieee754Remainder(EntityTransformHelper.GetTwistAngleAroundZ(recentering), MathF.Tau);
            twistMin = middle - joint.TwistLimit.Max;
            twistMax = middle + joint.TwistLimit.Max;
        }

        var twistMinDegrees = float.RadiansToDegrees(twistMin);
        var twistMaxDegrees = float.RadiansToDegrees(twistMax);

        var node = MakeNode(className,
            ("parent_body", parentName),
            ("child_body", childName),
            ("anchor_origin", ToKVArray(joint.Frame1.Position)),
            ("anchor_angles", ToKVArray(EntityTransformHelper.ToEulerAngles(anchorRotation))),
            ("collision_enabled", joint.EnableCollision));

        if (!legacy)
        {
            node.Add("use_block_solver", joint.Flags.HasFlag(JointFlags.UseBlockSolver));

            if (joint.IsAngularConstraintDisabled)
            {
                node.Add("constraint_space", "linear_only");
            }
            else if (joint.IsLinearConstraintDisabled)
            {
                node.Add("constraint_space", "angular_only");
            }
        }

        switch (joint.Type)
        {
            case JointType.Conical:
                node.Add("enable_swing_limit", IsLimitEnabled(joint.EnableSwingLimit, joint.SwingLimit.Min, joint.SwingLimit.Max));
                node.Add("swing_limit", float.RadiansToDegrees(joint.SwingLimit.Max));
                node.Add("enable_twist_limit", IsLimitEnabled(joint.EnableTwistLimit, twistMin, twistMax));
                node.Add("min_twist_angle", twistMinDegrees);
                node.Add("max_twist_angle", twistMaxDegrees);

                if (hasBodyRotations)
                {
                    var offset = Quaternion.Inverse(parentRotation * joint.Frame1.Rotation) * (childRotation * joint.Frame2.Rotation);
                    node.Add("swing_offset_angle", ToKVArray(-EntityTransformHelper.ToEulerAngles(Quaternion.Normalize(offset))));
                }

                break;
            case JointType.Revolute:
                node.Add("enable_limit", IsLimitEnabled(joint.EnableTwistLimit, twistMin, twistMax));
                node.Add("min_angle", twistMinDegrees);
                node.Add("max_angle", twistMaxDegrees);
                break;
            case JointType.Prismatic:
                node.Add("enable_limit", IsLimitEnabled(joint.EnableLinearLimit, joint.LinearLimit.Min, joint.LinearLimit.Max));
                node.Add("min_offset", joint.LinearLimit.Min);
                node.Add("max_offset", joint.LinearLimit.Max);
                break;
            case JointType.Weld:
                // The compiler reads the weld's linear frequency under this misspelled key.
                node.Add("linear_freqeuncy", joint.LinearFrequency);
                node.Add("linear_damping_ratio", joint.LinearDampingRatio);
                node.Add("angular_frequency", joint.AngularFrequency);
                node.Add("angular_damping_ratio", joint.AngularDampingRatio);

                if (legacy)
                {
                    node.Add("max_force", joint.MaxForce);
                    node.Add("max_torque", joint.MaxTorque);
                }

                break;
            case JointType.Wheel:
                node.Add("linear_frequency", joint.LinearFrequency);
                node.Add("linear_damping_ratio", joint.LinearDampingRatio);
                node.Add("enable_suspension_limit", IsLimitEnabled(joint.EnableLinearLimit, joint.LinearLimit.Min, joint.LinearLimit.Max));
                node.Add("min_suspension_offset", joint.LinearLimit.Min);
                node.Add("max_suspension_offset", joint.LinearLimit.Max);
                node.Add("enable_steering_limit", IsLimitEnabled(joint.EnableTwistLimit, twistMin, twistMax));
                node.Add("min_steering_angle", twistMinDegrees);
                node.Add("max_steering_angle", twistMaxDegrees);
                node.Add("spin_axis_friction", joint.Friction);
                break;
        }

        if (surfaceProperties != null && HasFrictionMotors(joint) && joint.Body2 < physics.Parts.Length)
        {
            var mass = GetShapeMass(physics, physics.Parts[joint.Body2].Shape, surfaceProperties);

            if (mass > 0d)
            {
                node.Add("friction", (float)(joint.MaxForce / (FrictionMotorForcePerKilogram * mass)));
            }
        }

        if (!legacy)
        {
            AddMotionResistance(node, joint);

            if (!string.IsNullOrEmpty(joint.Tag))
            {
                node.Add("name", joint.Tag);
            }
        }

        return node;
    }

    private static bool HasFrictionMotors(Joint joint)
        => !joint.HasFriction && joint.MaxForce > 0f && joint.Type is JointType.Conical or JointType.Revolute or JointType.Prismatic;

    private static bool HasUnexportedMotors(Joint joint)
        => joint.HasFriction && (joint.EnableLinearMotor || joint.EnableAngularMotor || joint.MaxForce != 0f || joint.MaxTorque != 0f);

    private Dictionary<uint, SurfacePhysics>? LoadSurfaceProperties()
    {
        using var resource = fileLoader?.LoadFileCompiled("surfaceproperties/surfaceproperties.vsurf");

        if (resource?.DataBlock is not BinaryKV3 surfaceData)
        {
            return null;
        }

        var surfaces = new Dictionary<uint, KVObject>();

        foreach (var surface in surfaceData.Data.Root.GetArray("SurfacePropertiesList"))
        {
            surfaces.TryAdd((uint)surface.GetUnsignedIntegerProperty("m_nameHash"), surface);
        }

        var surfaceProperties = new Dictionary<uint, SurfacePhysics>();

        foreach (var (hash, surface) in surfaces)
        {
            if (GetInheritedPhysicsProperty(surfaces, surface, "density") is { } density)
            {
                surfaceProperties.Add(hash, new SurfacePhysics(density, GetInheritedPhysicsProperty(surfaces, surface, "thickness") ?? 0f));
            }
        }

        return surfaceProperties;
    }

    /// <summary>
    /// Gets a surface's physics property, or that of the nearest base surface that defines it.
    /// </summary>
    private static float? GetInheritedPhysicsProperty(Dictionary<uint, KVObject> surfaces, KVObject surface, string key)
    {
        var current = surface;

        for (var depth = 0; depth < surfaces.Count; depth++)
        {
            if (current.GetSubCollection("physics") is { } surfacePhysics && surfacePhysics.ContainsKey(key))
            {
                return surfacePhysics.GetFloatProperty(key);
            }

            if (!surfaces.TryGetValue((uint)current.GetUnsignedIntegerProperty("m_baseNameHash"), out var baseSurface))
            {
                return null;
            }

            current = baseSurface;
        }

        return null;
    }

    /// <summary>
    /// Gets the mass a legacy compiler derives from a body's sphere, capsule and hull shapes to turn joint friction
    /// into motors: a shell of the surface's thickness when it has one, otherwise the solid volume.
    /// </summary>
    private static double GetShapeMass(PhysAggregateData physics, Shape shape, IReadOnlyDictionary<uint, SurfacePhysics> surfaceProperties)
    {
        var mass = 0d;

        foreach (var sphere in shape.Spheres)
        {
            double radius = sphere.Shape.Radius;
            mass += GetSurfaceMass(physics, surfaceProperties, sphere.SurfacePropertyIndex,
                4d / 3d * Math.PI * radius * radius * radius, 4d * Math.PI * radius * radius);
        }

        foreach (var capsule in shape.Capsules)
        {
            double radius = capsule.Shape.Radius;
            double height = Vector3.Distance(capsule.Shape.Center[0], capsule.Shape.Center[1]);
            mass += GetSurfaceMass(physics, surfaceProperties, capsule.SurfacePropertyIndex,
                Math.PI * radius * radius * (height + 4d / 3d * radius), 2d * Math.PI * radius * (height + 2d * radius));
        }

        foreach (var hull in shape.Hulls)
        {
            mass += GetSurfaceMass(physics, surfaceProperties, hull.SurfacePropertyIndex, hull.Shape.Volume, GetSurfaceArea(hull.Shape));
        }

        return mass;
    }

    private static double GetSurfaceMass(PhysAggregateData physics, IReadOnlyDictionary<uint, SurfacePhysics> surfaceProperties,
        int surfacePropertyIndex, double volume, double area)
    {
        var hashes = physics.SurfacePropertyHashes;

        if (surfacePropertyIndex < 0 || surfacePropertyIndex >= hashes.Length
            || !surfaceProperties.TryGetValue(hashes[surfacePropertyIndex], out var surface))
        {
            return UnknownSurfaceKilogramsPerCubicInch * volume;
        }

        return surface.Density * CubicInchesToCubicMeters * (surface.Thickness > 0f ? area * surface.Thickness : volume);
    }

    private static double GetSurfaceArea(Hull hull)
    {
        var positions = hull.GetVertexPositions();
        var edges = hull.GetEdges();
        var area = 0d;

        foreach (var face in hull.GetFaces())
        {
            foreach (var (a, b, c) in Hull.GetFaceTriangles(edges, face))
            {
                area += MathUtils.TriangleCross(positions[a], positions[b], positions[c]).Length() * 0.5d;
            }
        }

        return area;
    }

    private static bool IsLimitEnabled(bool compiledEnable, float min, float max)
        => compiledEnable || min != 0f || max != 0f;

    private static Quaternion GetRotation(Matrix4x4 transform)
        => Matrix4x4.Decompose(transform, out _, out var rotation, out _) ? rotation : Quaternion.CreateFromRotationMatrix(transform);

    private static void AddMotionResistance(KVObject node, Joint joint)
    {
        if (joint.Plasticity != 0f)
        {
            node.Add("motion_resistance", "plastic");
            node.Add("elasticity", joint.Elasticity);
            node.Add("elastic_damping", joint.ElasticDamping);
            node.Add("plasticity", joint.Plasticity);
        }
        else if (joint.Elasticity != 0f || joint.ElasticDamping != 1f)
        {
            node.Add("motion_resistance", "elastic");
            node.Add("elasticity", joint.Elasticity);
            node.Add("elastic_damping", joint.ElasticDamping);
        }
        else if (joint.Type != JointType.Wheel && joint.Friction != 0f)
        {
            node.Add("motion_resistance", "friction");
            node.Add("friction", joint.Friction);
        }
        else
        {
            node.Add("motion_resistance", "none");
        }
    }

    /// <summary>
    /// Adds one body markup for every body in <see cref="GetMarkedUpPhysicsBodies"/>, targeting it with the spelling and
    /// tag of the <c>CPhysicsBodyGameMarkupData</c> entry that targets that body.
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
                var targetBody = parentBone;
                KVObject? gameMarkup = null;

                if (gameMarkups.TryGetValue(parentBone, out var entry))
                {
                    (targetBody, gameMarkup) = entry;
                }

                lists.PhysicsBodyMarkup.Add(BuildPhysicsBodyMarkup(partsData[i], targetBody, gameMarkup));
            }
        }
    }

    /// <summary>
    /// Gets the bodies that get a body markup: every named body that a <c>CPhysicsBodyGameMarkupData</c> entry targets
    /// or whose part fields differ from what the compiler writes for a body without markup.
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
            var gameMarkups = GetPhysicsBodyGameMarkups();
            var partsData = physics.Data.GetArray("m_parts");

            for (var i = 0; i < physics.Parts.Length; i++)
            {
                var parentBone = physics.GetParentBoneName(i);

                if (parentBone.Length > 0 && (gameMarkups.ContainsKey(parentBone) || IsPartAuthored(physics.Parts[i], partsData[i])))
                {
                    bodies.Add(parentBone);
                }
            }
        }

        return markedUpPhysicsBodies = bodies;
    }

    /// <summary>
    /// Gets the <c>CPhysicsBodyGameMarkupData</c> entries keyed by the body they target, each with the target body as
    /// the entry spells it.
    /// </summary>
    private Dictionary<string, (string TargetBody, KVObject Markup)> GetPhysicsBodyGameMarkups()
    {
        var gameMarkups = new Dictionary<string, (string TargetBody, KVObject Markup)>(StringComparer.OrdinalIgnoreCase);
        var markups = model?.KeyValues.GetSubCollection("CPhysicsBodyGameMarkupData")?.GetSubCollection("m_PhysicsBodyMarkupByBoneName");

        if (markups != null)
        {
            foreach (var (name, markup) in markups)
            {
                var targetBody = markup.GetStringProperty("m_TargetBody", name!);
                gameMarkups[targetBody] = (targetBody, markup);
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

/// <summary>
/// The density and shell thickness a surface property's physics block defines.
/// </summary>
internal readonly record struct SurfacePhysics(float Density, float Thickness);
