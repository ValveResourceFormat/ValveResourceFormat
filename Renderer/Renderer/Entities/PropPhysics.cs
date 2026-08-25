using Box3D;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A physically simulated prop, Source's <c>CPhysicsProp</c>: it falls, tumbles, gets shoved by the
/// player, and can be picked up and carried with +USE the way Half-Life 2 does it. The model and its
/// collision shape come from <see cref="BaseModelEntity"/>; the movement comes from a dynamic body in
/// <see cref="PhysicsSimulation"/> whose pose the entity adopts every tick.
/// </summary>
public class PropPhysics : BaseModelEntity
{
    /// <summary>The <c>spawnflags</c> a physics prop reads.</summary>
    [Flags]
    private enum PropSpawnFlags : uint
    {
        /// <summary>The body waits for a touch before it starts simulating.</summary>
        StartAsleep = 1,

        /// <summary>The prop never simulates; it stands as a static obstacle other props collide with.</summary>
        MotionDisabled = 8,
    }

    /// <summary>Gets the rigid body simulating this prop. Only meaningful while <see cref="HasBody"/>.</summary>
    public Body Body => body;

    // The struct's setters mutate native state, which C# will not allow through a property copy
    private Body body;

    /// <summary>Gets whether a rigid body was built; a prop whose model carries no collision has none.</summary>
    public bool HasBody { get; private set; }

    /// <summary>Gets whether the player can pick this prop up: it has a body, and the body simulates.</summary>
    public bool CanBeCarried => HasBody && body.Type == BodyType.Dynamic;

    /// <summary>Gets the player carrying this prop, or <see langword="null"/> when nobody is.</summary>
    public PlayerEntity? Carrier { get; private set; }

    /// <summary>Gets whether the player is carrying this prop right now.</summary>
    public bool IsCarried => Carrier != null;

    /// <summary>Gets how far ahead of the eyes the mass center is held while carried.</summary>
    public float CarryDistance { get; private set; }

    /// <summary>
    /// Gets where the carry steering last sent the body. A free body lands there within the tick,
    /// so the gap between this and where the body actually is measures how blocked it is - the
    /// carry's drop test and the drawing's glue both read it.
    /// </summary>
    public Vector3 LastSteeredPosition { get; private set; }

    // While the body keeps up with its steering, the prop is drawn glued to the carry pose; this
    // much shortfall means something physical stopped it (a wall, mostly) and the true pose wins
    // outright, with a blend between
    private const float GlueFullDistance = 6f;
    private const float GlueBreakDistance = 24f;

    // The attach glide: how fast the grabbed prop flies to the hold pose, bounded so a close grab
    // still eases and a far one does not take all day
    private const float AttachSpeed = 900f;
    private const float MinAttachDuration = 0.15f;
    private const float MaxAttachDuration = 0.4f;

    // How close to the eyes the hold point may be pulled when a wall is in the way
    private const float MinHoldDistance = 16f;

    // The grab in the body's frame: where the mass center sits (the hold point steers the mass
    // center, not the body origin, so the prop hangs centered under the crosshair) and how the body
    // was oriented relative to the view when grabbed
    private Vector3 carryLocalMassCenter;
    private Quaternion carryRelativeRotation;

    // The attach glide's fixed end: where the body was grabbed, and when, so the carry pose can
    // ease from there to the hold pose instead of yanking the prop over in one tick
    private Vector3 carryStartPosition;
    private Quaternion carryStartRotation;
    private float carryStartTime;
    private float carryAttachDuration;

    // Roughly how much room the prop needs, for the wall trace to leave in front of a hit
    private float carryBoundsRadius;

    /// <summary>
    /// Initializes the prop from its keyvalues.
    /// </summary>
    public PropPhysics(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

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
        }
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        if (!HasBody || body.Type != BodyType.Dynamic)
        {
            return;
        }

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
        Carrier = carrier;
        CarryDistance = carryDistance;
        carryLocalMassCenter = body.LocalCenterOfMass;
        carryRelativeRotation = Quaternion.Inverse(ViewRotation(carrier.Controller.ViewForward)) * body.Rotation;

        // The glide in: from where it stands now to the hold pose, over a time set by how far
        // that is, so a distant grab pulls the prop over rather than teleporting it to hand
        carryStartPosition = body.Position;
        carryStartRotation = body.Rotation;
        carryStartTime = EntitySystem.CurrentTime;
        carryBoundsRadius = body.Bounds.Extents.Length();
        LastSteeredPosition = body.Position;

        var (holdPosition, _) = ComputeHoldPose();
        carryAttachDuration = Math.Clamp(Vector3.Distance(body.Position, holdPosition) / AttachSpeed,
            MinAttachDuration, MaxAttachDuration);

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
    /// Where the carried body belongs at a moment in time: the hold pose in front of the eyes,
    /// eased in from the grab pose while the attach glide is still running. Read per tick to steer
    /// the body and per frame to draw it, so both follow the same live view.
    /// </summary>
    /// <param name="time">The <see cref="EntitySystem.CurrentTime"/> moment to evaluate at.</param>
    /// <returns>The body-origin position and rotation of the carry pose.</returns>
    internal (Vector3 Position, Quaternion Rotation) ComputeCarryPose(float time)
    {
        var (position, rotation) = ComputeHoldPose();
        var attach = Math.Clamp((time - carryStartTime) / carryAttachDuration, 0f, 1f);

        if (attach >= 1f)
        {
            return (position, rotation);
        }

        // Smoothstep, so the glide leaves the grab gently and arrives gently
        var ease = attach * attach * (3f - 2f * attach);

        return (Vector3.Lerp(carryStartPosition, position, ease),
            Quaternion.Slerp(carryStartRotation, rotation, ease));
    }

    /// <summary>
    /// The hold pose alone: the mass center on the eye ray, the grab orientation turned with the
    /// view. The engine's grab controller traces the view and pulls the hold point in front of
    /// whatever it hits, and so does this: a target that is never inside a wall is what keeps the
    /// rigid chase from pressing the prop through one.
    /// </summary>
    private (Vector3 Position, Quaternion Rotation) ComputeHoldPose()
    {
        var controller = Carrier!.Controller;
        var eyePosition = controller.EyePosition;
        var forward = controller.ViewForward;
        var distance = CarryDistance;

        if (EntitySystem.PhysicsOrNull is { } physics)
        {
            // Static geometry only: props (this one included) and the player must not shorten
            // their own hold distance
            var hit = physics.World.RaycastClosest(eyePosition, forward * distance,
                new QueryFilter(PhysicsSimulation.PlayerCategory, PhysicsSimulation.StaticCategory));

            if (hit.Hit)
            {
                distance = Math.Max(hit.Fraction * distance - carryBoundsRadius, MinHoldDistance);
            }
        }

        var rotation = ViewRotation(forward) * carryRelativeRotation;

        // Offsetting by the rotated local mass center is what puts the *center* of the prop under
        // the crosshair, wherever its body origin happens to sit
        var position = eyePosition + forward * distance
            - Vector3.Transform(carryLocalMassCenter, rotation);

        return (position, rotation);
    }

    /// <summary>
    /// Records where the carry steering sent the body this tick, for the blockage measures.
    /// </summary>
    internal void MarkSteeredTo(Vector3 position)
    {
        LastSteeredPosition = position;
    }

    /// <inheritdoc/>
    protected override bool UpdatesRenderTransformEveryFrame => IsCarried;

    /// <summary>
    /// Draws a carried prop glued to the carry pose instead of a tick behind it. The eyes move per
    /// rendered frame while the body moves per tick, so drawing the body's pose leaves a held prop
    /// visibly trailing every camera turn. The glue is keyed off how far the body fell short of
    /// where steering sent it - not off its distance to the live pose, which a fast view turn
    /// inflates for a tick and made the drawing flicker between the two - so it only lets the
    /// physics pose back in when something real (a wall) is holding the body back.
    /// </summary>
    protected override void UpdateRenderTransform(float fraction)
    {
        if (Carrier is { IsRemoved: false })
        {
            var time = EntitySystem.CurrentTime + fraction * EntitySystem.TickInterval;
            var (carryPosition, carryRotation) = ComputeCarryPose(time);

            var shortfall = Vector3.Distance(body.Position, LastSteeredPosition);
            var glue = Math.Clamp((GlueBreakDistance - shortfall) / (GlueBreakDistance - GlueFullDistance), 0f, 1f);

            if (glue > 0f)
            {
                SetRenderTransform(
                    Vector3.Lerp(body.Position, carryPosition, glue),
                    Quaternion.Slerp(body.Rotation, carryRotation, glue));
                return;
            }
        }

        base.UpdateRenderTransform(fraction);
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
