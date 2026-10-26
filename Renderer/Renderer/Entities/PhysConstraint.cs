using Box3D;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>Base of the constraint entities: a joint between the <c>attach1</c> and <c>attach2</c> bodies, where a blank name is the world.</summary>
public abstract class PhysConstraint : BaseEntity
{
    [Flags]
    private enum ConstraintSpawnFlags : uint
    {
        /// <summary>The two attached bodies pass through each other.</summary>
        DisableCollision = 1,

        // 2 is defined by each kind of constraint

        /// <summary>The constraint waits for <c>TurnOn</c>.</summary>
        StartInactive = 4,

        /// <summary>The bodies are not connected until <c>TurnOn</c>.</summary>
        NoConnectUntilTurnedOn = 16,
    }

    /// <summary>Scales a break or notify limit, authored as a weight in pounds, to a force.</summary>
    protected const float LimitForceScale = 0.45359237f * PhysicsSimulation.GravityValue;

    /// <summary>Scales a friction torque in newton-metres to map units.</summary>
    protected const float FrictionTorqueScale = PhysicsSimulation.UnitsPerMeter * PhysicsSimulation.UnitsPerMeter;

    private Joint joint;
    private bool hasJoint;
    private Body worldFreedBody;
    private bool hasWorldFreedBody;
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
        forceLimit = KeyValues.GetFloatProperty("forcelimit") * LimitForceScale;
        torqueLimit = KeyValues.GetFloatProperty("torquelimit") * LimitForceScale;
    }

    // Not in Spawn, as the attached entities may be authored after this one
    /// <inheritdoc/>
    public override void Activate()
    {
        // Bodies authored asleep stay asleep
        if (!HasSpawnFlags(ConstraintSpawnFlags.StartInactive) && !HasSpawnFlags(ConstraintSpawnFlags.NoConnectUntilTurnedOn))
        {
            TurnOn(wakeBodies: false);
        }
    }

    /// <summary>Gets the world axis the joint turns about, the Z axis of both joint frames.</summary>
    protected virtual Vector3 JointAxis => Vector3.Transform(Vector3.UnitZ, EntityTransformHelper.EulerAnglesToQuaternion(Angles));

    /// <summary>Gets the world point the two bodies are joined at.</summary>
    protected virtual Vector3 JointAnchor => Origin;

    /// <summary>Gets whether the two attached bodies collide with each other.</summary>
    protected virtual bool CollidesConnected => !HasSpawnFlags(ConstraintSpawnFlags.DisableCollision);

    /// <summary>Gets whether a body attached to the world without collision stops colliding with all static geometry.</summary>
    protected virtual bool FreesFromWorldGeometry => true;

    /// <summary>Gets whether the load on the joint this tick breaks the constraint.</summary>
    protected virtual bool IsOverloaded(Joint joint, float tickInterval)
        => (forceLimit > 0f && joint.ConstraintForce.Length() > forceLimit)
            || (torqueLimit > 0f && joint.ConstraintTorque.Length() > torqueLimit);

    // Frictionless joints would swing forever; a quarter unit lever settles them within a few swings
    private const float FallbackFrictionLever = 0.25f;

    /// <summary>Gets the torque limit of a zero speed motor standing in for joint friction, with a small fallback when none is authored.</summary>
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

    /// <summary>Creates the joint for the connection, or <see langword="null"/> when the authored settings have no equivalent.</summary>
    protected abstract Joint? CreateJoint(Box3D.PhysicsWorld world, JointDefinition connection);

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
            CollideConnected = CollidesConnected,
        };

        if (CreateJoint(physics.World, connection) is { } created)
        {
            joint = created;
            hasJoint = true;

            // The world is one body: disabling world collision means disabling all static geometry
            if (FreesFromWorldGeometry && !connection.CollideConnected && (body1.Equals(physics.WorldAnchor) || body2.Equals(physics.WorldAnchor)))
            {
                worldFreedBody = body1.Equals(physics.WorldAnchor) ? body2 : body1;
                hasWorldFreedBody = true;
                PhysicsSimulation.SetCollidesWith(worldFreedBody, PhysicsSimulation.StaticCategory, collide: false);
            }

            if (wakeBodies)
            {
                joint.WakeBodies();
            }
        }
    }

    // Joints stretch apart past about this mass ratio
    private const float MaxMassRatio = 10f;

    // Raising the lighter body barely shows on a wire or hinge pin, unlike a heavy body falling through its joint
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

    // A name that finds no body fails rather than pinning the other side to the world
    internal static bool TryResolveAttachment(BaseEntity self, PhysicsSimulation physics, string key, out Body body)
    {
        var name = self.SpawnData?.GetStringProperty(key);

        if (string.IsNullOrEmpty(name))
        {
            body = physics.WorldAnchor;
            return true;
        }

        foreach (var entity in self.EntitySystem.FindAllByTargetName(name))
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

        // Destroying an attached body destroys the joint
        if (!joint.IsValid)
        {
            hasJoint = false;
            return;
        }

        if (IsOverloaded(joint, tickInterval))
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

        if (hasWorldFreedBody)
        {
            hasWorldFreedBody = false;

            if (worldFreedBody.IsValid)
            {
                PhysicsSimulation.SetCollidesWith(worldFreedBody, PhysicsSimulation.StaticCategory, collide: true);
            }
        }
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
