using System.Collections.Generic;
using Box3D;
using ValveResourceFormat.Renderer.Input;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The player, as an entity the rest of the world can see. Source's <c>CBasePlayer</c> is an entity like
/// any other; here the movement itself still lives behind an <see cref="IPlayerController"/>, and this
/// mirrors it into the entity world so triggers have something to touch and teleports something to move.
/// </summary>
/// <remarks>
/// Position comes from the controller rather than being simulated: the player moves off the input, per
/// rendered frame, not on the entity tick. This entity is only a view onto that state, so
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

    // Contacts are detected by what the solver did to last tick's command: gravity is off while
    // carried, so a free body keeps exactly the velocity it was handed, and any difference is a
    // contact's doing. The softness lingers briefly so a scraping carry does not flicker between
    // the regimes at the tick rate.
    private const float ContactVelocityTolerance = 5f;
    private const float ContactAngularTolerance = 0.5f;
    private const float CarrySoftDuration = 0.2f;

    // How long the prop may strain far from the hold pose before the player loses hold of it.
    // The window also covers the attach: a full-reach grab needs about a third of a second to
    // accelerate over, and only a grab the world refuses should run out the clock.
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

    /// <summary>
    /// Gets the buttons as of the current tick: what is held, and what changed since the tick before.
    /// </summary>
    private PlayerButtonState Buttons;

    // The kinematic body standing where the player stands, so walking into props shoves them
    private Body presenceBody;
    private bool hasPresenceBody;

    // The debris shove: ragdoll parts never meet the pushing body in the solver (a fast overlap
    // ejects thin parts through the mesh floor), so walking through them applies a horizontal
    // velocity instead, which cannot press anything into the ground
    private const float MinShoveSpeed = 20f;
    private const float MaxShoveSpeed = 350f;
    private const float ShoveMargin = 2f;

    // How long the carried prop has been stuck far from its hold pose; a flick spikes this for a
    // tick or two, a wedged prop keeps it climbing until the carry gives up
    private float carryStrainTime;

    // Last frame's commanded velocities and how much longer the chase stays soft, for the
    // contact detection above
    private Vector3 carryCommandedVelocity;
    private Vector3 carryCommandedAngularVelocity;
    private float carrySoftTime;

    /// <summary>
    /// Creates the player entity for a movement controller.
    /// </summary>
    public PlayerEntity(EntitySystem system, Scene scene, IPlayerController controller) : base(system, scene, "player")
    {
        Controller = controller;

        // Nothing traces against the player, and the player is what enters triggers rather than a volume
        // anything can enter.
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
    /// Teleports the player. <see cref="BaseEntity.Origin"/> is the feet, which is what
    /// <see cref="IPlayerController.Teleport"/> takes, so the destination passes straight through.
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

        // The controller owns the position, so there is nothing to integrate; just keep up with it
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
        Origin = Controller.Position;
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
    /// Shoves ragdoll parts the player's hull overlaps with a purely horizontal velocity, away
    /// from the player at the player's own speed. This replaces solver contact for ragdolls: a
    /// kinematic box overlapping a part lying on the floor resolves out the box's nearest face,
    /// which at running speed is the bottom, ejecting the part through the one-sided mesh world.
    /// A horizontal velocity can never press anything into the ground.
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

        // Collected first, shoved after: the world is locked while a query runs. Queried as a
        // prop rather than as the player, whose category the ragdoll shapes filter out.
        var overlapped = new DebrisOverlap { Physics = physics };
        physics.World.OverlapBox(center, halfExtents, ref overlapped,
            new QueryFilter(PhysicsSimulation.PropCategory, PhysicsSimulation.PropCategory));

        var heldBody = Carried?.CarryBody;
        var pushSpeed = MathF.Min(speed, MaxShoveSpeed);

        foreach (var body in overlapped.Bodies)
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

    private struct DebrisOverlap : IOverlapCallback
    {
        public required PhysicsSimulation Physics;
        public List<Body> Bodies { get; } = [];

        public DebrisOverlap()
        {
        }

        public readonly bool OnOverlap(Shape shape)
        {
            var body = shape.Body;

            // Only ragdolls: props still collide with the pushing body and need no shove
            if (Physics.GetOwner(body) is RagdollProp && !Bodies.Contains(body))
            {
                Bodies.Add(body);
            }

            return true;
        }
    }

    /// <summary>
    /// The +USE carry, the way Half-Life 2's shadow controller holds what the player picks up: a
    /// target velocity that would land the body on the hold pose. Free of contacts it is handed
    /// to the body outright, so the prop rides the view rigidly; while something touches the
    /// body, the velocity may only change by a bounded acceleration from whatever the solver
    /// left it with. The bound is what makes contacts stick: a wall that stopped the prop stays
    /// in charge, because the carry can only lean back in a step at a time - it presses, never
    /// rams. The body keeps its steered velocity on release, so dropping mid-stride carries the
    /// player's motion and a view flick is a throw.
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

        // A prop that stays far from its hold pose is wedged somewhere it cannot leave; the
        // engine drops what it cannot keep hold of, but only after a strain the flick of a
        // throw never sustains. The attach approach counts as strain too, so a grab the world
        // will not let through gives up rather than grinding.
        if (Vector3.Distance(body.Position, holdPosition) > CarryBreakDistance)
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

        if (carried.CarriesOrientation)
        {
            var disturbed = (body.LinearVelocity - carryCommandedVelocity).Length() > ContactVelocityTolerance
                || (body.AngularVelocity - carryCommandedAngularVelocity).Length() > ContactAngularTolerance;

            carrySoftTime = disturbed ? CarrySoftDuration : MathF.Max(carrySoftTime - deltaTime, 0f);
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

        carryCommandedVelocity = SteerVelocity(
            body.LinearVelocity,
            holdPosition - body.Position,
            deltaTime, MaxCarrySpeed,
            soft ? MaxCarryAcceleration : float.PositiveInfinity);

        body.LinearVelocity = carryCommandedVelocity;

        if (carried.CarriesOrientation)
        {
            carryCommandedAngularVelocity = SteerVelocity(
                body.AngularVelocity,
                RotationError(body.Rotation, holdRotation),
                deltaTime, MaxCarryAngularSpeed,
                soft ? MaxCarryAngularAcceleration : float.PositiveInfinity);

            body.AngularVelocity = carryCommandedAngularVelocity;
        }
    }

    // One axis of the shadow controller: the velocity that lands the body on the pose within the
    // step about to run - no feed-forward on top, which extrapolated a frame ahead and made the
    // held prop lead the view by one frame's motion - speed-capped, approached from the current
    // velocity by at most maxAcceleration over the step
    private static Vector3 SteerVelocity(Vector3 current, Vector3 error,
        float deltaTime, float maxSpeed, float maxAcceleration)
    {
        var target = error / deltaTime;
        var targetSpeed = target.Length();

        if (targetSpeed > maxSpeed)
        {
            target *= maxSpeed / targetSpeed;
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
                PhysicsSimulation.StaticCategory | PhysicsSimulation.PropCategory | PhysicsSimulation.MoverCategory));

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
        carryable.BeginCarry(this, HoldDistance + Vector3.Distance(hit.Point, grabbedBody.CenterOfMass), grabbedBody);

        var body = carryable.CarryBody;
        carryCommandedVelocity = body.LinearVelocity;
        carryCommandedAngularVelocity = body.AngularVelocity;
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
