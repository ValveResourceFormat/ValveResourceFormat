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

    // The hold pose as the last two ticks saw it, so the drawing can subtract the tick-rate
    // camera out of the tick-rate body pose and re-base what remains - the physical deviation -
    // onto the live per-frame camera
    private (Vector3 Position, Quaternion Rotation) carryTickHold;
    private (Vector3 Position, Quaternion Rotation) carryTickHoldPrevious;

    // The drawn deviation chases the measured one with this time constant, framerate
    // independently. The filter is what keeps 64 Hz out of the picture: the measured deviation
    // steps at the tick rate, the smoothed one cannot, and free carry decays it to zero so the
    // prop rides the frame camera exactly.
    private const float DeviationSmoothingTime = 0.05f;

    // The smoothed deviation itself, persisted across frames
    private Vector3 smoothedCarryDeviation;
    private Quaternion smoothedCarryDeviationRotation = Quaternion.Identity;

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

        // The hold pose this tick saw, kept alongside the body pose it produced, so the drawing
        // can tell how much of the body's pose is the camera and how much is physics
        if (IsCarried)
        {
            carryTickHoldPrevious = carryTickHold;
            carryTickHold = ComputeHoldPose();
        }
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
        carryRelativeRotation = Quaternion.Inverse(ViewRotation(carrier.Controller.ViewAngles)) * body.Rotation;

        // Off the pushing body, so the held prop cannot wedge against its carrier
        SetCollidesWithPlayer(false);

        body.GravityScale = 0f;
        body.CanSleep = false;
        body.IsAwake = true;

        carryTickHold = carryTickHoldPrevious = ComputeHoldPose();

        // The drawn deviation starts as the real one - the prop is still standing wherever it
        // was grabbed - and decays as the body flies in, which is the attach as the player sees it
        smoothedCarryDeviation = body.Position - carryTickHold.Position;
        smoothedCarryDeviationRotation = body.Rotation * Quaternion.Inverse(carryTickHold.Rotation);
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

        // Back to plain interpolation next frame; snapping the history trims the one-frame hop
        // from the camera-glued pose to the tick-lagged one when dropped mid-stride
        SnapInterpolation();
    }

    /// <summary>
    /// Relaxes the grip part way toward the body's current orientation, for when the world is
    /// twisting the held prop away from the hold rotation: the twist gradually becomes the
    /// carried orientation instead of an error the carry keeps fighting.
    /// </summary>
    /// <param name="fraction">How much of the way to the body's orientation the grip moves.</param>
    internal void AdoptCarryRotation(float fraction)
    {
        var view = ViewRotation(Carrier!.Controller.ViewAngles);
        var oldRelative = carryRelativeRotation;
        carryRelativeRotation = Quaternion.Slerp(oldRelative, Quaternion.Inverse(view) * body.Rotation, fraction);

        // The recorded tick holds move with the grip, so the deviation the drawing subtracts
        // shrinks by exactly what the grip absorbed and the rendered pose stays continuous
        var gripChange = Quaternion.Inverse(oldRelative) * carryRelativeRotation;
        carryTickHold.Rotation *= gripChange;
        carryTickHoldPrevious.Rotation *= gripChange;

        // The drawn deviation counter-rotates by the hold rotation's own move, so the relaxing
        // grip does not read as motion: what it absorbed leaves the picture too
        var counter = view * oldRelative * Quaternion.Inverse(carryRelativeRotation) * Quaternion.Inverse(view);
        smoothedCarryDeviationRotation = Quaternion.Normalize(smoothedCarryDeviationRotation * counter);
    }

    /// <inheritdoc/>
    protected override bool UpdatesRenderTransformEveryFrame => IsCarried;

    /// <summary>
    /// Draws the carried prop against the live camera instead of a tick behind it. The tick-rate
    /// hold pose is subtracted out of the tick-rate body pose, leaving only the physical
    /// deviation the solver imposed - the tracking lag, or a wall in the way - and that deviation
    /// is re-based onto the hold pose of the frame's own camera. Held free, the deviation is near
    /// zero and the prop is glued to the crosshair with no 64 Hz quantization; held against an
    /// obstacle, the full deviation shows, changing only at tick rate and interpolated like any
    /// other physics.
    /// </summary>
    protected override void UpdateRenderTransform(float fraction)
    {
        if (!IsCarried)
        {
            base.UpdateRenderTransform(fraction);
            return;
        }

        var (tickPosition, tickRotation) = InterpolateTickPose(fraction);
        var holdPosition = Vector3.Lerp(carryTickHoldPrevious.Position, carryTickHold.Position, fraction);
        var holdRotation = Quaternion.Slerp(carryTickHoldPrevious.Rotation, carryTickHold.Rotation, fraction);

        // The FRAME'S camera, not the controller's: view smoothing and view punch sit between the
        // input camera the carry steers by and the camera the frame is drawn with, and a hold pose
        // computed from the wrong one leaves the prop trailing the view by exactly that gap
        var (livePosition, liveRotation) = EntitySystem.RenderCamera is { } camera
            ? ComputeHoldPose(camera.Location, camera.Forward, camera.GetQAngle())
            : ComputeHoldPose();

        // World-frame deviations: a prop pressed against a wall stays pressed against that wall
        // while the camera keeps moving
        var deviation = tickPosition - holdPosition;
        var deviationRotation = tickRotation * Quaternion.Inverse(holdRotation);

        // The physics only ever saw the camera at 64 Hz, so the measured deviation steps at the
        // tick rate - the body chased last tick's hold pose, and a high-fps view changes speed
        // every frame. The drawing follows a low-pass filtered copy instead: the tick staircase
        // cannot pass the filter, free carry decays it to zero so the prop rides the frame
        // camera exactly, and a real obstruction fades in over the time constant.
        var alpha = 1f - MathF.Exp(-Math.Clamp(EntitySystem.FrameInterval, 0f, 0.1f) / DeviationSmoothingTime);

        smoothedCarryDeviation = Vector3.Lerp(smoothedCarryDeviation, deviation, alpha);
        smoothedCarryDeviationRotation = Quaternion.Slerp(smoothedCarryDeviationRotation, deviationRotation, alpha);

        SetRenderTransform(
            livePosition + smoothedCarryDeviation,
            Quaternion.Normalize(smoothedCarryDeviationRotation) * liveRotation);
    }

    /// <summary>
    /// Where the carried body belongs by the carrier's input camera, which is what the carry
    /// steers the body toward on the tick.
    /// </summary>
    internal (Vector3 Position, Quaternion Rotation) ComputeHoldPose()
    {
        var controller = Carrier!.Controller;

        return ComputeHoldPose(controller.EyePosition, controller.ViewForward, controller.ViewAngles);
    }

    /// <summary>
    /// Where the carried body belongs for a given view: the mass center on the eye ray at the
    /// carry distance, the grab orientation turned with the view. There is no attach glide - the
    /// carry's bounded acceleration is what pulls a distant grab over smoothly - and the pose is
    /// not clamped against the world: aiming into a wall asks for a pose inside it, the solver's
    /// contacts hold the body at the surface, and a prop held far from an unreachable pose for
    /// long enough is let go by the strain drop.
    /// </summary>
    private (Vector3 Position, Quaternion Rotation) ComputeHoldPose(Vector3 eyePosition, Vector3 forward, Vector3 viewAngles)
    {
        var rotation = ViewRotation(viewAngles) * carryRelativeRotation;

        // Offsetting by the rotated local mass center is what puts the *center* of the prop under
        // the crosshair, wherever its body origin happens to sit. The body's own rotation, not the
        // target's: while the turn is still catching up, aiming the origin with the target
        // rotation would push the actual mass center off the ray and set position and rotation
        // fighting each other.
        var position = eyePosition + forward * CarryDistance
            - Vector3.Transform(carryLocalMassCenter, body.Rotation);

        return (position, rotation);
    }

    /// <summary>
    /// The view as a rotation, pitch and yaw only, so a carried prop turns with the whole view
    /// the way the gravgun's held objects do. Built from the view angles rather than the forward
    /// vector: a reconstructed yaw degenerates looking straight down, and the whipping target
    /// rotation used to spin the carried prop there.
    /// </summary>
    internal static Quaternion ViewRotation(Vector3 viewAngles)
        => EntityTransformHelper.EulerAnglesToQuaternion(new Vector3(viewAngles.X, viewAngles.Y, 0f));

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
