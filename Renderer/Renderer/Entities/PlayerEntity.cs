using Box3D;
using ValveResourceFormat.Renderer.Input;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The player as an entity, mirroring an <see cref="IPlayerController"/> so triggers can touch it
/// and teleports can move it.
/// </summary>
/// <remarks>
/// The controller owns the position and moves per rendered frame, not on the entity tick, so
/// <see cref="TryGetTouchBounds"/> reads the hull live rather than the last tick's copy.
/// </remarks>
public sealed class PlayerEntity : BaseEntity
{
    /// <summary>How far the player can reach to press something.</summary>
    public float UseRange { get; set; } = 80f;

    // The +USE carry: how far the pickup trace reaches, and how far ahead of the mass center the
    // base hold point sits
    private const float PickupReach = 300f;
    private const float HoldDistance = 40f;

    // The shadow controller: the held body is asked for the velocity that lands it exactly on
    // this frame's hold pose - the world steps right after the ask, so this is neither lag nor
    // lead - clamped to the speed caps. Free of contacts the chase is rigid, the velocity set
    // outright; while something is touching the body, its velocity may only change by the
    // acceleration caps per second. Updating the solver's velocity with a bounded step is what
    // makes contacts stick: a wall that zeroed the approach stays in charge, and the carry can
    // only lean back in gently. The speed caps also set how hard a view flick throws.
    private const float MaxCarrySpeed = 1000f;
    private const float MaxCarryAcceleration = 6000f;
    private const float MaxCarryAngularSpeed = 25f;
    private const float MaxCarryAngularAcceleration = 150f;

    // Contacts are detected by how far the body fell short of where last frame's step should have
    // put it: gravity is off while carried, so a free body lands where its velocity takes it, and
    // whatever is left over a contact took. Position, not velocity, because the solver pushes a
    // penetrating body out by moving it, leaving its velocity untouched. The tolerance covers
    // damping, a fixed part plus a share of the step. The softness lingers briefly so a scraping
    // carry does not flicker between the regimes.
    private const float ContactDistanceTolerance = 0.1f;
    private const float ContactAngleTolerance = 0.01f;
    private const float ContactStepTolerance = 0.05f;
    private const float CarrySoftDuration = 0.2f;

    // A fresh pickup travels to the hold pose at these caps, braking to arrive rather than
    // snapping there, and the rigid chase only takes over once it has arrived
    private const float PickupTravelSpeed = 500f;
    private const float PickupTravelAcceleration = 4000f;
    private const float PickupTurnSpeed = 8f;
    private const float PickupTurnAcceleration = 60f;
    private const float PickupArriveDistance = 1f;
    private const float PickupArriveAngle = 0.05f;

    // How long the prop may strain far from the hold pose before the player loses hold of it.
    // A pickup still travelling there only strains while something blocks it.
    private const float CarryBreakDistance = 64f;
    private const float CarryBreakTime = 0.5f;

    // While contacts are acting, the grip relaxes toward the orientation the world forces on the
    // prop with this time constant, so a plank dragged along a doorframe rotates to fit and
    // stays that way. Free carry never relaxes, which keeps view turns exact.
    private const float GripRelaxTime = 0.25f;

    /// <summary>Gets the controller whose state this entity reflects.</summary>
    public IPlayerController Controller { get; }

    /// <summary>Gets what the player is carrying, or <see langword="null"/> when their hands are free.</summary>
    public ICarryable? Carried { get; private set; }

    // Held and changed buttons, latched once per tick
    private PlayerButtonState Buttons;

    // The kinematic body standing where the player stands, so walking into props shoves them
    private Body presenceBody;
    private bool hasPresenceBody;

    // The debris shove, see ShoveDebris
    private const float MinShoveSpeed = 20f;
    private const float MaxShoveSpeed = 350f;
    private const float ShoveMargin = 2f;

    // Reused by the debris shove every tick
    private readonly List<Body> debrisBodies = [];
    private readonly HashSet<ulong> debrisBodyIds = [];

    // How long the carried prop has been stuck far from its hold pose; a flick spikes this for a
    // tick or two, a wedged prop keeps it climbing until the carry gives up
    private float carryStrainTime;

    // Where last frame's step should leave the body, and how much longer the chase stays soft, for
    // the contact detection above
    private Vector3 carryExpectedPosition;
    private Quaternion? carryExpectedRotation;
    private float carryExpectedStep;
    private float carryExpectedTurn;
    private bool hasCarryExpectation;
    private float carrySoftTime;

    // Whether the pickup has reached the hold pose, after which the chase is rigid
    private bool carryArrived;

    /// <summary>Creates the player entity for a movement controller.</summary>
    public PlayerEntity(EntitySystem system, Scene scene, IPlayerController controller) : base(system, scene, "player")
    {
        Controller = controller;

        // Traces ignore the player, which enters triggers rather than being one
        IsSolid = false;
    }

    /// <inheritdoc/>
    public override bool TryGetTouchBounds(out Vector3 center, out Vector3 halfExtents)
    {
        halfExtents = Controller.HullHalfExtents;
        center = Controller.Position + new Vector3(0, 0, halfExtents.Z);
        return true;
    }

    /// <summary>
    /// Teleports the player. <see cref="BaseEntity.WorldOrigin"/> is the feet, as in
    /// <see cref="IPlayerController.Teleport"/>, so the destination is passed through unchanged.
    /// </summary>
    public override void Teleport(Vector3 origin, Vector3? angles)
    {
        Controller.Teleport(origin, angles);
        SyncFromController();
    }

    /// <summary>
    /// Presses whatever the player is looking at within <see cref="UseRange"/>, the <c>+use</c> command.
    /// </summary>
    /// <returns>The entity that was pressed, or <see langword="null"/> if nothing was in reach.</returns>
    public BaseEntity? PressUse()
    {
        var from = Controller.EyePosition;
        var target = EntitySystem.FindUseTarget(from, from + Controller.ViewForward * UseRange);

        target?.Use(this);

        return target;
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        // Resets key latches inside player movement, if a key is presseed,
        // unpressed and pressed again in within 3 frames the tick will see it as one press.
        Buttons = Controller.ConsumeButtons();

        // The controller owns the position, nothing to integrate
        SyncFromController();

        if (Buttons.Pressed(TrackedKeys.E))
        {
            // The engine's +use order: hands full means let go, a usable entity in reach wins the
            // press, and only a press nothing answered becomes a pickup attempt
            if (Carried != null)
            {
                Drop();
            }
            else if (PressUse() == null)
            {
                TryPickup();
            }
        }

        UpdatePhysicsPresence(tickInterval);
    }

    /// <inheritdoc/>
    internal override void FrameSimulate(float frameTime)
    {
        // Per frame, not per tick: the carry chases the camera, and a chase sampled at 64 Hz
        // reads back quantized however smoothly it is drawn
        UpdateCarry(frameTime);
    }

    private void SyncFromController()
    {
        WorldOrigin = Controller.Position;
        Velocity = Controller.Velocity;
    }

    /// <summary>
    /// Keeps the kinematic pushing body where the player stands. Moving it with a velocity rather
    /// than teleporting it is what lets the solver shove props out of the way with the player's
    /// real speed; a jump across the map is not a shove, so that snaps instead.
    /// </summary>
    private void UpdatePhysicsPresence(float tickInterval)
    {
        // Only once something else has built the rigid body world; a player in a map with no
        // physics props has nothing to push
        if (EntitySystem.PhysicsOrNull is not { } physics)
        {
            return;
        }

        if (!hasPresenceBody)
        {
            presenceBody = physics.CreatePlayerBody(Origin, Controller.HullHalfExtents);
            hasPresenceBody = true;
            return;
        }

        if (Vector3.DistanceSquared(presenceBody.Position, Origin) > 256f * 256f)
        {
            presenceBody.SetTransform(Origin, null);
        }
        else
        {
            presenceBody.MoveTowards(Origin, Quaternion.Identity, tickInterval, wake: true);
        }

        ShoveDebris(physics);
    }

    /// <summary>
    /// Shoves the debris the player's hull overlaps with a purely horizontal velocity, away from
    /// the player at the player's own speed. Debris never meets the pushing body in the solver
    /// (see <see cref="PhysicsSimulation.DebrisCategory"/>), and a horizontal velocity can never
    /// press anything into the ground.
    /// </summary>
    private void ShoveDebris(PhysicsSimulation physics)
    {
        var horizontalVelocity = Velocity with { Z = 0f };
        var speed = horizontalVelocity.Length();

        // A still player is not a jitter source for whatever they stand in
        if (speed < MinShoveSpeed)
        {
            return;
        }

        var halfExtents = Controller.HullHalfExtents + new Vector3(ShoveMargin, ShoveMargin, 0f);
        var center = Origin + new Vector3(0f, 0f, Controller.HullHalfExtents.Z);

        // Collected first, shoved after: the world is locked while a query runs
        debrisBodies.Clear();
        debrisBodyIds.Clear();

        var overlapped = new DebrisOverlap(debrisBodies, debrisBodyIds);
        physics.World.OverlapBox(center, halfExtents, ref overlapped,
            new QueryFilter(PhysicsSimulation.PlayerCategory, PhysicsSimulation.DebrisCategory));

        var heldBody = Carried?.CarryBody;
        var pushSpeed = MathF.Min(speed, MaxShoveSpeed);

        foreach (var body in debrisBodies)
        {
            // The carried body is the carry's to steer, not the hull's to shove
            if (body.UserData == heldBody?.UserData)
            {
                continue;
            }

            var direction = (body.Position - center) with { Z = 0f };
            var distance = direction.Length();

            // Radially away from the player, or along the motion when standing dead centered
            direction = distance > 0.5f ? direction / distance : horizontalVelocity / speed;

            var current = Vector3.Dot(body.LinearVelocity, direction);

            if (current < pushSpeed)
            {
                var pushed = body;
                pushed.LinearVelocity += direction * (pushSpeed - current);
                pushed.IsAwake = true;
            }
        }
    }

    // One entry per body however many of its shapes overlap
    private readonly struct DebrisOverlap(List<Body> bodies, HashSet<ulong> bodyIds) : IOverlapCallback
    {
        public bool OnOverlap(Shape shape)
        {
            var body = shape.Body;

            if (bodyIds.Add(body.UserData))
            {
                bodies.Add(body);
            }

            return true;
        }
    }

    /// <summary>
    /// The +USE carry, the way Half-Life 2's shadow controller holds what the player picks up: a
    /// target velocity that would land the body on the hold pose. A fresh pickup travels there
    /// within capped speeds first. Once there and free of contacts the velocity is handed to the
    /// body outright, so the prop rides the view rigidly; while something touches the body, the
    /// velocity may only change by a bounded acceleration from whatever the solver left it with.
    /// The bound is what makes contacts stick: a wall that stopped the prop stays in charge,
    /// because the carry can only lean back in a step at a time - it presses, never rams. The
    /// body keeps its steered velocity on release, so dropping mid-stride carries the player's
    /// motion and a view flick is a throw.
    /// </summary>
    private void UpdateCarry(float deltaTime)
    {
        // A zero-length frame has no step to steer for, and dividing by it would hand the
        // solver a NaN it can never recover from
        if (deltaTime <= 1e-6f || Carried is not { } carried)
        {
            return;
        }

        if (carried.IsRemoved)
        {
            // Gone from the world mid-carry; nothing left to restore state on
            Carried = null;
            return;
        }

        var body = carried.CarryBody;
        var (holdPosition, holdRotation) = carried.ComputeHoldPose();
        var blocked = IsCarryBlocked(body);

        carryArrived = carryArrived
            || (Vector3.Distance(body.Position, holdPosition) < PickupArriveDistance
                && (holdRotation is not { } arriveRotation || RotationError(body.Rotation, arriveRotation).Length() < PickupArriveAngle));

        // A prop that stays far from its hold pose is wedged somewhere it cannot leave; the
        // engine drops what it cannot keep hold of, but only after a strain the flick of a
        // throw never sustains
        if (Vector3.Distance(body.Position, holdPosition) > CarryBreakDistance && (carryArrived || blocked))
        {
            carryStrainTime += deltaTime;

            if (carryStrainTime >= CarryBreakTime)
            {
                Drop();
                return;
            }
        }
        else
        {
            carryStrainTime = 0f;
        }

        // A carry without orientation - a grabbed ragdoll part - skips the contact softness
        // altogether: the rest of the ragdoll hangs off the held part through its joints, which
        // reads as a permanent contact, and a chase capped to the soft acceleration could never
        // hold that weight up. The chain itself is the compliance a wall needs.
        var soft = false;

        if (holdRotation is not null)
        {
            carrySoftTime = blocked ? CarrySoftDuration : MathF.Max(carrySoftTime - deltaTime, 0f);
            soft = carrySoftTime > 0f;

            // The world re-shaping the grip: while contacts are acting, the grip continuously
            // relaxes toward the orientation the world is forcing, rather than springing back
            // forever. Continuous rather than threshold-triggered on purpose: a threshold re-latch
            // fired repeatedly during a turn and stepped the prop's rotation in visible quanta.
            if (soft)
            {
                carried.AdoptCarryRotation(1f - MathF.Exp(-deltaTime / GripRelaxTime));
                (_, holdRotation) = carried.ComputeHoldPose();
            }
        }

        // Rigid once arrived and free of contacts; travelling there, or pressed on, within the caps
        var (maxSpeed, maxAngularSpeed) = carryArrived
            ? (MaxCarrySpeed, MaxCarryAngularSpeed)
            : (PickupTravelSpeed, PickupTurnSpeed);
        var (maxAcceleration, maxAngularAcceleration) = soft ? (MaxCarryAcceleration, MaxCarryAngularAcceleration)
            : carryArrived ? (float.PositiveInfinity, float.PositiveInfinity)
            : (PickupTravelAcceleration, PickupTurnAcceleration);

        var velocity = SteerVelocity(body.LinearVelocity, holdPosition - body.Position, deltaTime, maxSpeed, maxAcceleration);
        body.LinearVelocity = velocity;

        carryExpectedPosition = body.Position + velocity * deltaTime;
        carryExpectedStep = velocity.Length() * deltaTime;
        carryExpectedRotation = null;
        hasCarryExpectation = true;

        if (holdRotation is { } rotation)
        {
            var angularVelocity = SteerVelocity(body.AngularVelocity, RotationError(body.Rotation, rotation),
                deltaTime, maxAngularSpeed, maxAngularAcceleration);
            body.AngularVelocity = angularVelocity;

            carryExpectedRotation = Turn(body.Rotation, angularVelocity * deltaTime);
            carryExpectedTurn = angularVelocity.Length() * deltaTime;
        }
    }

    // Whether a contact took part of last frame's step: the body fell short of where its own
    // velocity should have carried it, or was pushed somewhere else
    private bool IsCarryBlocked(Body body)
    {
        if (!hasCarryExpectation)
        {
            return false;
        }

        var missed = Vector3.Distance(body.Position, carryExpectedPosition);

        if (missed > ContactDistanceTolerance + (ContactStepTolerance * carryExpectedStep))
        {
            return true;
        }

        // Only when the carry steers the turn too; a held ragdoll part swings on its joints
        return carryExpectedRotation is { } expected
            && RotationError(expected, body.Rotation).Length() > ContactAngleTolerance + (ContactStepTolerance * carryExpectedTurn);
    }

    // Turns an orientation by a world-space rotation vector, axis times angle
    private static Quaternion Turn(Quaternion rotation, Vector3 turn)
    {
        var angle = turn.Length();

        return angle < 1e-8f
            ? rotation
            : Quaternion.Normalize(Quaternion.CreateFromAxisAngle(turn / angle, angle) * rotation);
    }

    /// <summary>
    /// The view a carried object is held in front of: the camera the frame is drawn with when
    /// there is one, since view smoothing and punch sit between the input camera and the drawn
    /// view, and a carry steered by the wrong one trails the picture by exactly that gap.
    /// </summary>
    internal (Vector3 EyePosition, Vector3 Forward, Vector3 ViewAngles) GetHoldView()
    {
        if (EntitySystem.RenderCamera is { } camera)
        {
            return (camera.Location, camera.Forward, camera.GetQAngle());
        }

        return (Controller.EyePosition, Controller.ViewForward, Controller.ViewAngles);
    }

    // One axis of the shadow controller: the velocity that lands the body on the pose within the
    // step about to run - no feed-forward on top, which extrapolated a frame ahead and made the
    // held prop lead the view by one frame's motion - speed-capped, approached from the current
    // velocity by at most maxAcceleration over the step. With a finite acceleration the speed is
    // also held to what can still brake to a stop at the pose, so it arrives instead of
    // overshooting.
    private static Vector3 SteerVelocity(Vector3 current, Vector3 error,
        float deltaTime, float maxSpeed, float maxAcceleration)
    {
        var target = error / deltaTime;
        var targetSpeed = target.Length();
        var speedCap = float.IsFinite(maxAcceleration)
            ? MathF.Min(maxSpeed, MathF.Sqrt(2f * maxAcceleration * error.Length()))
            : maxSpeed;

        if (targetSpeed > speedCap)
        {
            target *= speedCap / targetSpeed;
        }

        var step = target - current;
        var stepLength = step.Length();
        var stepCap = maxAcceleration * deltaTime;

        if (stepLength > stepCap)
        {
            step *= stepCap / stepLength;
        }

        return current + step;
    }

    // The shortest-arc rotation between two orientations as a world-space rotation vector (axis
    // times angle), the angular twin of a position error
    private static Vector3 RotationError(Quaternion from, Quaternion to)
    {
        var delta = to * Quaternion.Inverse(from);

        if (delta.W < 0f)
        {
            // Both q and -q are the same rotation; the negative one is the long way round
            delta = Quaternion.Negate(delta);
        }

        var axis = new Vector3(delta.X, delta.Y, delta.Z);
        var axisLength = axis.Length();

        if (axisLength < 1e-8f)
        {
            return Vector3.Zero;
        }

        return axis * (2f * MathF.Atan2(axisLength, delta.W) / axisLength);
    }

    private void TryPickup()
    {
        if (EntitySystem.PhysicsOrNull is not { } physics)
        {
            return;
        }

        var eyePosition = Controller.EyePosition;
        var forward = Controller.ViewForward;

        // Cast against the world and the movers too, not just props, so a crate behind a wall or
        // a closed door is not grabbable through it: the obstacle wins the raycast and the pickup
        // finds nothing
        var hit = physics.World.RaycastClosest(eyePosition, forward * PickupReach,
            new QueryFilter(PhysicsSimulation.PlayerCategory,
                PhysicsSimulation.StaticCategory | PhysicsSimulation.PropCategory
                | PhysicsSimulation.DebrisCategory | PhysicsSimulation.MoverCategory));

        if (!hit.Hit)
        {
            return;
        }

        var grabbedBody = hit.Shape.Body;

        if (physics.GetOwner(grabbedBody) is not ICarryable carryable || carryable.IsRemoved || !carryable.CanBeCarried)
        {
            return;
        }

        // The sandbox gravgun's hold distance: a fixed reach plus how far the grabbed surface sits
        // from the mass center, so a big crate hangs further out than a soda can and neither clips
        // the player's hull. The grabbed body's own center, which for a ragdoll is the one part
        // under the crosshair rather than the whole body.
        Carried = carryable;
        carryStrainTime = 0f;
        carrySoftTime = 0f;
        hasCarryExpectation = false;
        carryArrived = false;
        carryable.BeginCarry(this, HoldDistance + Vector3.Distance(hit.Point, grabbedBody.CenterOfMass), grabbedBody);
    }

    private void Drop()
    {
        if (Carried is { IsRemoved: false } carried)
        {
            // Lets go rather than throws: the body keeps the chase velocity it was carried with -
            // the player's own motion plus whatever a view flick added - which the carry's speed
            // cap already bounds
            carried.EndCarry();
        }

        carryStrainTime = 0f;
        Carried = null;
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        Drop();

        if (hasPresenceBody)
        {
            presenceBody.Destroy();
            hasPresenceBody = false;
        }
    }
}
