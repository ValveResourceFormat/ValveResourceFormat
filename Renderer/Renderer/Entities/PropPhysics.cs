using Box3D;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A physically simulated prop, Source's <c>CPhysicsProp</c>: it falls, tumbles, gets shoved by the
/// player, and can be picked up and carried with +USE the way Half-Life 2 does it. The model and its
/// collision shape come from <see cref="BaseModelEntity"/>; the movement comes from a dynamic body in
/// <see cref="PhysicsSimulation"/> whose pose the entity adopts every tick.
/// </summary>
public class PropPhysics : BaseModelEntity
{
    /// <summary>The <c>spawnflags</c> a physics prop reads, Source's <c>SF_PHYSPROP_*</c>.</summary>
    [Flags]
    private enum PropSpawnFlags : uint
    {
        /// <summary>The body waits for a touch before it starts simulating.</summary>
        StartAsleep = 1,

        /// <summary>
        /// The prop does not simulate until something enables its motion; being grabbed counts,
        /// the way the gravity gloves free a pinned prop. Until then it stands as a static
        /// obstacle other props collide with.
        /// </summary>
        MotionDisabled = 8,

        /// <summary>The player can never pick this prop up, as authored.</summary>
        PreventPickup = 512,
    }

    /// <summary>Gets the rigid body simulating this prop. Only meaningful while <see cref="HasBody"/>.</summary>
    public Body Body => body;

    // The struct's setters mutate native state, which C# will not allow through a property copy
    private Body body;

    /// <summary>Gets whether a rigid body was built; a prop whose model carries no collision has none.</summary>
    public bool HasBody { get; private set; }

    /// <summary>
    /// Gets whether the player can pick this prop up: it has a body, and the map did not forbid
    /// it. A motion-disabled prop counts - grabbing it is what enables its motion, the way the
    /// gravity gloves free a prop pinned in place.
    /// </summary>
    public bool CanBeCarried => HasBody && !HasSpawnFlags(PropSpawnFlags.PreventPickup);

    /// <summary>Gets the player carrying this prop, or <see langword="null"/> when nobody is.</summary>
    public PlayerEntity? Carrier { get; private set; }

    /// <summary>Gets whether the player is carrying this prop right now.</summary>
    public bool IsCarried => Carrier != null;

    /// <summary>Gets how far ahead of the eyes the mass center is held while carried.</summary>
    public float CarryDistance { get; private set; }

    // The grab in the body's frame: where the mass center sits (the hold point steers the mass
    // center, not the body origin, so the prop hangs centered under the crosshair) and how the body
    // was oriented relative to the view when grabbed
    private Vector3 carryLocalMassCenter;
    private Quaternion carryRelativeRotation;

    // Roughly how much room the prop needs, for the wall trace to leave in front of a hit
    private float carryBoundsRadius;

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

        // The world stepped at the start of this tick; adopt the pose it produced. The entity's
        // interpolation then draws the frames in between, and the collider follows so the player
        // keeps colliding with the prop wherever it tumbles to.
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

    /// <summary>
    /// Puts the prop in the player's hands: gravity lets go, the pushing body stops colliding with
    /// it, and the carry logic in <see cref="PlayerEntity"/> starts steering the body.
    /// </summary>
    /// <param name="carrier">The player doing the carrying.</param>
    /// <param name="carryDistance">How far ahead of the eyes the mass center is held.</param>
    public void BeginCarry(PlayerEntity carrier, float carryDistance)
    {
        // Grabbing a motion-disabled prop enables its motion, permanently: once freed it stays a
        // simulating body, which is what the engine's EnableMotion on grab amounts to
        if (body.Type != BodyType.Dynamic)
        {
            body.Type = BodyType.Dynamic;
            EntitySystem.TriggerOutput(this, "OnMotionEnabled", carrier);
        }

        EntitySystem.TriggerOutput(this, "OnPlayerPickup", carrier);

        Carrier = carrier;
        CarryDistance = carryDistance;
        carryLocalMassCenter = body.LocalCenterOfMass;
        carryRelativeRotation = Quaternion.Inverse(ViewRotation(carrier.Controller.ViewForward)) * body.Rotation;
        carryBoundsRadius = body.Bounds.Extents.Length();

        // Off the pushing body, so the held prop cannot wedge against its carrier
        SetCollidesWithPlayer(false);

        body.GravityScale = 0f;
        body.CanSleep = false;
        body.IsAwake = true;
    }

    /// <summary>
    /// Lets go of the prop, restoring gravity and collision. The body keeps whatever velocity the
    /// carry left it with, which is how a walking or turning player lends the prop their motion.
    /// </summary>
    public void EndCarry()
    {
        Carrier = null;
        SetCollidesWithPlayer(true);

        body.GravityScale = 1f;
        body.CanSleep = true;
        body.IsAwake = true;
    }

    /// <summary>
    /// Where the carried body belongs: the mass center on the eye ray, the grab orientation turned
    /// with the view. The engine's grab controller traces the view and pulls the hold point in
    /// front of whatever it hits, and so does this: a target that is never inside a wall is what
    /// keeps the chase from pressing the prop through one. There is no attach glide - the carry's
    /// bounded acceleration is what pulls a distant grab over smoothly.
    /// </summary>
    internal (Vector3 Position, Quaternion Rotation) ComputeHoldPose()
    {
        var controller = Carrier!.Controller;
        var eyePosition = controller.EyePosition;
        var forward = controller.ViewForward;
        var distance = CarryDistance;

        if (EntitySystem.PhysicsOrNull is { } physics)
        {
            // The prop's bounding sphere swept along the aim, against the world and the movers, so
            // a closed door shortens the hold like a wall does; props (this one included) and the
            // player must not shorten their own hold. The solver's own geometry finds the farthest
            // pose the shape still fits at, which clamps oblique walls, edges and corners
            // correctly with no backoff arithmetic. Jammed against a wall the free spot is
            // honestly near the eye, and a fully wedged grab is what the strain drop is for.
            var fraction = physics.World.CastCapsule(
                new Capsule(Vector3.Zero, Vector3.Zero, carryBoundsRadius),
                eyePosition, forward * distance,
                new QueryFilter(PhysicsSimulation.PlayerCategory,
                    PhysicsSimulation.StaticCategory | PhysicsSimulation.MoverCategory));

            distance *= fraction;
        }

        var rotation = ViewRotation(forward) * carryRelativeRotation;

        // Offsetting by the rotated local mass center is what puts the *center* of the prop under
        // the crosshair, wherever its body origin happens to sit. The body's own rotation, not the
        // target's: while the turn is still catching up, aiming the origin with the target
        // rotation would push the actual mass center off the ray and set position and rotation
        // fighting each other.
        var position = eyePosition + forward * distance
            - Vector3.Transform(carryLocalMassCenter, body.Rotation);

        return (position, rotation);
    }

    /// <summary>
    /// The view direction as a rotation, pitch and yaw only, so a carried prop turns with the
    /// whole view the way the gravgun's held objects do.
    /// </summary>
    internal static Quaternion ViewRotation(Vector3 forward)
    {
        var pitch = float.RadiansToDegrees(-MathF.Asin(Math.Clamp(forward.Z, -1f, 1f)));
        var yaw = float.RadiansToDegrees(MathF.Atan2(forward.Y, forward.X));

        return EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(pitch, yaw, 0f));
    }

    private void SetCollidesWithPlayer(bool collide)
    {
        var collidesWith = collide
            ? ulong.MaxValue
            : ulong.MaxValue & ~PhysicsSimulation.PlayerCategory;

        Span<Shape> shapes = stackalloc Shape[body.ShapeCount];
        var count = body.GetShapes(shapes);

        for (var i = 0; i < count; i++)
        {
            shapes[i].SetFilter(new CollisionFilter(PhysicsSimulation.PropCategory, collidesWith, 0), recomputeContacts: true);
        }
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
