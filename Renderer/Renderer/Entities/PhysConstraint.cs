using Box3D;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The physics constraints, Source's <c>CPhysConstraint</c>: a joint in <see cref="PhysicsSimulation"/>
/// between the bodies of the entities <c>attach1</c> and <c>attach2</c> name, anchored at this entity's
/// origin. A blank name stands for the world. Limits measure <c>attach2</c> relative to
/// <c>attach1</c>, which is how the solver measures its second body against its first.
/// </summary>
public abstract class PhysConstraint : BaseEntity
{
    /// <summary>The <c>spawnflags</c> every constraint reads, Source's <c>SF_CONSTRAINT_*</c>.</summary>
    [Flags]
    private enum ConstraintSpawnFlags : uint
    {
        /// <summary>The two attached bodies pass through each other.</summary>
        DisableCollision = 1,

        /// <summary>The constraint waits for <c>TurnOn</c>.</summary>
        StartInactive = 2,
    }

    // The break limits are authored in pounds, the weight the constraint can hold before it gives
    private const float PoundsToKilograms = 0.45359237f;

    /// <summary>
    /// Joint friction is authored as a torque in newton-metres; the solver works in kilograms and map
    /// units, so it scales by the square of the unit length.
    /// </summary>
    protected const float FrictionTorqueScale = PhysicsSimulation.UnitsPerMeter * PhysicsSimulation.UnitsPerMeter;

    private Joint joint;
    private bool hasJoint;
    private bool isBroken;
    private float forceLimit;
    private float torqueLimit;

    /// <summary>Initializes a constraint from its keyvalues.</summary>
    protected PhysConstraint(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Gets whether the constraint currently holds its bodies together.</summary>
    public bool IsActive => hasJoint;

    /// <inheritdoc/>
    public override void Spawn()
    {
        var weight = PoundsToKilograms * PhysicsSimulation.GravityValue;

        forceLimit = KeyValues.GetFloatProperty("forcelimit") * weight;
        torqueLimit = KeyValues.GetFloatProperty("torquelimit") * weight;
    }

    // In Activate, as the attached entities may be authored after this one and need their bodies
    /// <inheritdoc/>
    public override void Activate()
    {
        // Built as the map spawned its bodies: a picture authored asleep on its hinge stays asleep
        if (!HasSpawnFlags(ConstraintSpawnFlags.StartInactive))
        {
            TurnOn(wakeBodies: false);
        }
    }

    /// <summary>
    /// Gets the direction the joint turns about in the world, the Z axis of both joint frames. A
    /// ball socket or a weld does not care; a hinge overrides it with its authored axis.
    /// </summary>
    protected virtual Vector3 JointAxis => Vector3.Transform(Vector3.UnitZ, EntityTransformHelper.EulerAnglesToQuaternion(Angles));

    /// <summary>Gets the point in the world the two bodies are joined at: the entity's origin, unless the joint says otherwise.</summary>
    protected virtual Vector3 JointAnchor => Origin;

    // A frictionless joint still loses its swing, the way drag would take it: this is the torque of
    // the held body's weight on a lever of a quarter unit. A picture on a nail then stops within a few
    // swings, hanging no more than a degree or two off plumb, rather than swinging for good.
    private const float FallbackFrictionLever = 0.25f;

    /// <summary>
    /// The resisting torque for a joint's authored friction, scaled from newton-metres. A joint
    /// authored without friction gets a small one sized to the lightest moving body it holds.
    /// </summary>
    protected static float FrictionTorque(float authoredFriction, in JointDefinition connection)
    {
        if (authoredFriction > 0f)
        {
            return authoredFriction * FrictionTorqueScale;
        }

        var mass = float.PositiveInfinity;

        foreach (var body in (ReadOnlySpan<Body>)[connection.BodyA, connection.BodyB])
        {
            if (body.Type == BodyType.Dynamic && body.Mass > 0f)
            {
                mass = MathF.Min(mass, body.Mass);
            }
        }

        return float.IsPositiveInfinity(mass) ? 0f : mass * PhysicsSimulation.GravityValue * FallbackFrictionLever;
    }

    /// <summary>
    /// Builds this constraint's kind of joint from the connection between the two bodies, which carries
    /// both joint frames and the collision setting.
    /// </summary>
    /// <returns>The joint, or <see langword="null"/> when the authored settings have no equivalent.</returns>
    protected abstract Joint? CreateJoint(PhysicsWorld world, JointDefinition connection);

    private void TurnOn(bool wakeBodies)
    {
        if (hasJoint || isBroken || EntitySystem.PhysicsOrNull is not { } physics)
        {
            return;
        }

        if (!TryResolveAttachment(this, physics, "attach1", out var body1) || !TryResolveAttachment(this, physics, "attach2", out var body2))
        {
            return;
        }

        if (body1.Equals(body2))
        {
            EntitySystem.Logger.LogDebug("{Classname} '{TargetName}' connects a body to itself", Classname, TargetName);
            return;
        }

        LimitMassRatio(body1, body2);

        var axis = JointAxis;

        if (axis.LengthSquared() < 1e-8f)
        {
            axis = Vector3.UnitZ;
        }

        var (frame1, frame2) = Joint.FramesFromWorldAnchor(body1, body2, JointAnchor, Vector3.Normalize(axis));

        var connection = JointDefinition.Connect(body1, body2, frame1, frame2) with
        {
            CollideConnected = !HasSpawnFlags(ConstraintSpawnFlags.DisableCollision),
        };

        if (CreateJoint(physics.World, connection) is { } created)
        {
            joint = created;
            hasJoint = true;

            if (wakeBodies)
            {
                joint.WakeBodies();
            }
        }
    }

    // The heaviest one jointed body may be against the other. The solver holds a joint well to
    // around this ratio and lets it stretch far past it: a two-metre metal chandelier weighs over a
    // tonne as solid hulls, and the half-kilogram wire it hangs from came apart by fifty units.
    private const float MaxMassRatio = 10f;

    // Raises the lighter of two dynamic bodies until the pair is within the ratio. The light one is
    // a wire, a hinge pin, a handle - its extra weight barely shows, while a heavy body falling
    // through its constraint does.
    private static void LimitMassRatio(Body body1, Body body2)
    {
        if (body1.Type != BodyType.Dynamic || body2.Type != BodyType.Dynamic)
        {
            return;
        }

        var (light, heavy) = body1.Mass < body2.Mass ? (body1, body2) : (body2, body1);

        if (light.Mass <= 0f || heavy.Mass <= light.Mass * MaxMassRatio)
        {
            return;
        }

        var scale = heavy.Mass / MaxMassRatio / light.Mass;
        var shapes = new Shape[light.ShapeCount];
        var count = light.GetShapes(shapes);

        for (var i = 0; i < count; i++)
        {
            shapes[i].SetDensity(shapes[i].Density * scale, updateBodyMass: true);
        }
    }

    /// <summary>
    /// Finds the body an <c>attach1</c>/<c>attach2</c> style key names, for anything that ties two
    /// bodies together. A blank name is the world; a name that finds nothing with a body fails,
    /// rather than silently pinning the other side to the world.
    /// </summary>
    internal static bool TryResolveAttachment(BaseEntity self, PhysicsSimulation physics, string key, out Body body)
    {
        var name = self.Data?.GetStringProperty(key);

        if (string.IsNullOrEmpty(name))
        {
            body = physics.WorldAnchor;
            return true;
        }

        foreach (var entity in self.EntitySystem.FindAllByTargetName(name, self.Scene))
        {
            if (entity is BaseModelEntity model && model.TryGetConstraintBody(self.Origin, out body))
            {
                return true;
            }
        }

        self.EntitySystem.Logger.LogDebug("{Classname} '{TargetName}' found no physics body named \"{Name}\" for {Key}", self.Classname, self.TargetName, name, key);
        body = default;
        return false;
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        base.PhysicsSimulate(tickInterval);

        if (!hasJoint)
        {
            return;
        }

        // A body it held was destroyed, which takes the joint with it
        if (!joint.IsValid)
        {
            hasJoint = false;
            return;
        }

        if ((forceLimit > 0f && joint.ConstraintForce.Length() > forceLimit)
            || (torqueLimit > 0f && joint.ConstraintTorque.Length() > torqueLimit))
        {
            Break(this);
        }
    }

    private void Break(BaseEntity? activator)
    {
        if (!hasJoint)
        {
            return;
        }

        DestroyJoint();
        isBroken = true;
        EntitySystem.TriggerOutput(this, "OnBreak", activator);
    }

    private void DestroyJoint()
    {
        if (!hasJoint)
        {
            return;
        }

        if (joint.IsValid)
        {
            joint.Destroy(wakeBodies: true);
        }

        hasJoint = false;
    }

    [EntityInput("Break")]
    private void InputBreak(EntityInputData data) => Break(data.Activator);

    [EntityInput("TurnOn")]
    private void InputTurnOn(EntityInputData data) => TurnOn(wakeBodies: true);

    [EntityInput("TurnOff")]
    private void InputTurnOff(EntityInputData data) => DestroyJoint();

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        DestroyJoint();
    }
}
