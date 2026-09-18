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
    private const float HoldDistance = 50f;

    // The shadow controller: the held body is asked to cover its pose error over SecondsToArrival,
    // clamped to the speed caps. Free of contacts the chase is rigid - the velocity is set
    // outright, so the prop rides the view with no trailing - but while something is touching
    // the body, its velocity may only change by the acceleration caps per second. Updating the
    // solver's velocity with a bounded step is what makes contacts stick: a wall that zeroed the
    // approach stays in charge, and the carry can only lean back in gently. The speed caps also
    // set how hard a view flick throws.
    private const float SecondsToArrival = 0.1f;
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

    // When the world twists the held prop more than 10 degrees away from the grip, the grip
    // re-latches to the twisted orientation instead of fighting to twist back - but only while
    // the view turns slower than this, because during a flick the same gap is just the servo
    // catching up, and adopting it would bleed the turn out of the grip.
    private const float CarryAdoptRotationError = 10f * MathF.PI / 180f;
    private const float CarryAdoptViewQuiet = 2f;

    /// <summary>Gets the controller whose state this entity reflects.</summary>
    public IPlayerController Controller { get; }

    /// <summary>Gets the prop the player is carrying, or <see langword="null"/> when their hands are free.</summary>
    public PropPhysics? CarriedProp { get; private set; }

    /// <summary>
    /// Gets the buttons as of the current tick: what is held, and what changed since the tick before.
    /// </summary>
    private PlayerButtonState Buttons;

    // The kinematic body standing where the player stands, so walking into props shoves them
    private Body presenceBody;
    private bool hasPresenceBody;

    // How long the carried prop has been stuck far from its hold pose; a flick spikes this for a
    // tick or two, a wedged prop keeps it climbing until the carry gives up
    private float carryStrainTime;

    // The hold pose as of the last tick, whose motion feeds forward into the chase so a held prop
    // tracks a walking player without trailing by the servo's lag
    private Vector3 lastHoldPosition;
    private Quaternion lastHoldRotation;

    // Last tick's commanded velocities and how much longer the chase stays soft, for the
    // contact detection above
    private Vector3 carryCommandedVelocity;
    private Vector3 carryCommandedAngularVelocity;
    private float carrySoftTime;

    /// <summary>
    /// Creates the player entity for a movement controller.
    /// </summary>
    public PlayerEntity(EntitySystem system, IPlayerController controller) : base(system, "player")
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
            if (CarriedProp != null)
            {
                Drop();
            }
            else if (PressUse() == null)
            {
                TryPickup();
            }
        }

        UpdatePhysicsPresence(tickInterval);
        UpdateCarry(tickInterval);
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
    private void UpdateCarry(float tickInterval)
    {
        if (CarriedProp is not { } prop)
        {
            return;
        }

        if (prop.IsRemoved)
        {
            // Gone from the world mid-carry; nothing left to restore state on
            CarriedProp = null;
            return;
        }

        var body = prop.Body;
        var (holdPosition, holdRotation) = prop.ComputeHoldPose();

        // A prop that stays far from its hold pose is wedged somewhere it cannot leave; the
        // engine drops what it cannot keep hold of, but only after a strain the flick of a
        // throw never sustains. The attach approach counts as strain too, so a grab the world
        // will not let through gives up rather than grinding.
        if (Vector3.Distance(body.Position, holdPosition) > CarryBreakDistance)
        {
            carryStrainTime += tickInterval;

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

        // How the hold pose itself moved this tick: fed forward so a prop carried by a walking,
        // turning player rides along instead of trailing by the servo's catch-up lag - and a view
        // flick hands the prop the flick's speed, which is the throw
        var holdVelocity = (holdPosition - lastHoldPosition) / tickInterval;
        var holdAngularVelocity = RotationError(lastHoldRotation, holdRotation) / tickInterval;

        // The world re-shaping the grip: a contact that has twisted the prop well away from the
        // hold rotation makes the twisted orientation the held one
        if (holdAngularVelocity.Length() < CarryAdoptViewQuiet
            && RotationError(body.Rotation, holdRotation).Length() > CarryAdoptRotationError)
        {
            prop.AdoptCarryRotation();
            holdRotation = body.Rotation;

            // The hold rotation jumping to the adopted grip is not motion to feed forward
            holdAngularVelocity = Vector3.Zero;
        }

        lastHoldPosition = holdPosition;
        lastHoldRotation = holdRotation;

        var disturbed = (body.LinearVelocity - carryCommandedVelocity).Length() > ContactVelocityTolerance
            || (body.AngularVelocity - carryCommandedAngularVelocity).Length() > ContactAngularTolerance;

        carrySoftTime = disturbed ? CarrySoftDuration : MathF.Max(carrySoftTime - tickInterval, 0f);
        var soft = carrySoftTime > 0f;

        carryCommandedVelocity = SteerVelocity(
            body.LinearVelocity,
            holdPosition - body.Position, holdVelocity,
            tickInterval, MaxCarrySpeed,
            soft ? MaxCarryAcceleration : float.PositiveInfinity);

        carryCommandedAngularVelocity = SteerVelocity(
            body.AngularVelocity,
            RotationError(body.Rotation, holdRotation), holdAngularVelocity,
            tickInterval, MaxCarryAngularSpeed,
            soft ? MaxCarryAngularAcceleration : float.PositiveInfinity);

        body.LinearVelocity = carryCommandedVelocity;
        body.AngularVelocity = carryCommandedAngularVelocity;
    }

    // One axis of the shadow controller: the velocity that covers the error over SecondsToArrival
    // on top of the target's own motion, speed-capped, approached from the current velocity by at
    // most maxAcceleration in this tick
    private static Vector3 SteerVelocity(Vector3 current, Vector3 error, Vector3 feedForward,
        float tickInterval, float maxSpeed, float maxAcceleration)
    {
        var target = feedForward + error / SecondsToArrival;
        var targetSpeed = target.Length();

        if (targetSpeed > maxSpeed)
        {
            target *= maxSpeed / targetSpeed;
        }

        var step = target - current;
        var stepLength = step.Length();
        var stepCap = maxAcceleration * tickInterval;

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

        if (!hit.Hit || physics.GetOwner(hit.Shape.Body) is not PropPhysics prop || prop.IsRemoved || !prop.CanBeCarried)
        {
            return;
        }

        // The sandbox gravgun's hold distance: a fixed reach plus how far the grabbed surface sits
        // from the mass center, so a big crate hangs further out than a soda can and neither clips
        // the player's hull
        CarriedProp = prop;
        carryStrainTime = 0f;
        carrySoftTime = 0f;
        carryCommandedVelocity = prop.Body.LinearVelocity;
        carryCommandedAngularVelocity = prop.Body.AngularVelocity;
        prop.BeginCarry(this, HoldDistance + Vector3.Distance(hit.Point, prop.Body.CenterOfMass));

        // The feed-forward baseline: the hold pose as of the grab, so the first tick sees the
        // pose's motion as zero rather than a jump from wherever the last carry ended
        (lastHoldPosition, lastHoldRotation) = prop.ComputeHoldPose();
    }

    private void Drop()
    {
        if (CarriedProp is { IsRemoved: false } prop)
        {
            // Lets go rather than throws: the body keeps the chase velocity it was carried with -
            // the player's own motion plus whatever a view flick added - which the carry's speed
            // cap already bounds
            prop.EndCarry();
        }

        carryStrainTime = 0f;
        CarriedProp = null;
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
