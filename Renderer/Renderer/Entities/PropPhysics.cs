using Box3D;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A physically simulated prop, Source's <c>CPhysicsProp</c>: it falls, tumbles, and gets shoved by
/// the player. The model and its collision shape come from <see cref="BaseModelEntity"/>; the movement comes from a dynamic body in
/// <see cref="PhysicsSimulation"/> whose pose the entity adopts every tick.
/// </summary>
public sealed class PropPhysics : BaseModelEntity
{
    /// <summary>The <c>spawnflags</c> a physics prop reads, Source's <c>SF_PHYSPROP_*</c>.</summary>
    [Flags]
    private enum PropSpawnFlags : uint
    {
        /// <summary>The body waits for a touch before it starts simulating.</summary>
        StartAsleep = 1,

        /// <summary>
        /// The prop does not simulate until something enables its motion. Until then it stands as
        /// a static obstacle other props collide with.
        /// </summary>
        MotionDisabled = 8,
    }

    /// <summary>Gets the rigid body simulating this prop. Only meaningful while <see cref="HasBody"/>.</summary>
    public Body Body => body;

    // The struct's setters mutate native state, which C# will not allow through a property copy
    private Body body;

    /// <summary>Gets whether a rigid body was built; a prop whose model carries no collision has none.</summary>
    public bool HasBody { get; private set; }

    // The body's sleep state as of the last tick, for the OnAwakened edge
    private bool wasAwake;

    /// <summary>
    /// Initializes the prop from its keyvalues.
    /// </summary>
    public PropPhysics(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override bool UsesMoverBody => false;

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        // Soft collision only: the player shoves props through the kinematic pushing body in the
        // rigid body world, and props never enter the movement traces. A hard-blocking prop pinned
        // against a wall is a prop the player gets stuck on; a soft one just gets pushed or passed
        // through, which can never wedge the player.
        IsSolid = false;

        // The collider was built alongside the model; a prop compiled without physics stays where
        // the map put it, exactly like an unimplemented classname would. IsEmpty is deliberately
        // not checked: it only speaks for the traced hulls and meshes, and a sphere-collision prop
        // (a soccer ball, say) is "empty" to the tracer while being exactly what belongs here.
        if (Collider is not { } collider)
        {
            return;
        }

        var created = EntitySystem.Physics.CreatePropBody(
            collider.PhysicsData,
            collider.LocalBounds,
            Origin,
            EntityTransformHelper.EulerAnglesToQuaternion(Angles),
            motionEnabled: !HasSpawnFlags(PropSpawnFlags.MotionDisabled),
            startAsleep: HasSpawnFlags(PropSpawnFlags.StartAsleep),
            owner: this);

        if (created is { } newBody)
        {
            body = newBody;
            HasBody = true;
            wasAwake = newBody.IsAwake;
        }
    }

    // A body put to sleep stays where it is even when what held it up is gone, so maps wake the
    // lid of a pot when the pot breaks
    [EntityInput("Wake")]
    private void InputWake(EntityInputData data)
    {
        if (HasBody)
        {
            body.IsAwake = true;
        }
    }

    [EntityInput("Sleep")]
    private void InputSleep(EntityInputData data)
    {
        if (HasBody)
        {
            body.IsAwake = false;
        }
    }

    [EntityInput("EnableMotion")]
    private void InputEnableMotion(EntityInputData data)
    {
        if (HasBody && body.Type != BodyType.Dynamic)
        {
            body.Type = BodyType.Dynamic;
            body.IsAwake = true;
            EntitySystem.TriggerOutput(this, "OnMotionEnabled", data.Activator);
        }
    }

    [EntityInput("DisableMotion")]
    private void InputDisableMotion(EntityInputData data)
    {
        if (HasBody)
        {
            body.Type = BodyType.Static;
        }
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        if (!HasBody || body.Type != BodyType.Dynamic)
        {
            return;
        }

        // A sleeping prop coming to life is an authored event, Source's OnAwakened; the map may
        // have wired an alarm to the can the player knocked over
        var isAwake = body.IsAwake;

        if (isAwake && !wasAwake)
        {
            EntitySystem.TriggerOutput(this, "OnAwakened");
        }

        wasAwake = isAwake;

        // Adopt the pose the frame-stepped world has moved the body to, so the entity state and
        // its collider follow the prop wherever it tumbles. The drawing does not wait for this:
        // an awake body is drawn at its live pose every frame.
        SetOriginAndAngles(body.Position, EntityTransformHelper.ToEulerAngles(body.Rotation));
    }

    /// <inheritdoc/>
    public override void Teleport(Vector3 origin, Vector3? angles)
    {
        base.Teleport(origin, angles);

        if (HasBody)
        {
            body.SetTransform(Origin, EntityTransformHelper.EulerAnglesToQuaternion(Angles));
            body.LinearVelocity = Vector3.Zero;
            body.AngularVelocity = Vector3.Zero;
        }
    }

    /// <inheritdoc/>
    protected override bool UpdatesRenderTransformEveryFrame => HasBody;

    /// <summary>
    /// Draws an awake body at its live pose. The world steps with the rendered frame, so the
    /// body's pose IS this frame's pose - there is nothing to interpolate and no tick rate to
    /// see. A sleeping body has not moved since its tick state, which the base interpolation
    /// draws exactly.
    /// </summary>
    protected override void UpdateRenderTransform(float fraction)
    {
        if (HasBody && body.IsAwake)
        {
            SetRenderTransform(body.Position, body.Rotation);
            return;
        }

        base.UpdateRenderTransform(fraction);
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        if (HasBody)
        {
            EntitySystem.PhysicsOrNull?.Forget(Body);
            body.Destroy();
            HasBody = false;
        }
    }
}
