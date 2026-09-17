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

    // The +USE carry: how far the pickup trace reaches, how far ahead of the mass center the base
    // hold point sits, the shadow controller's speed caps (fast enough that a view flick is a
    // throw, bounded so a blocked prop presses on what stops it rather than ramming it every
    // tick), and how long the prop may strain far from the hold pose before the player loses
    // hold of it.
    private const float PickupReach = 300f;
    private const float HoldDistance = 50f;
    private const float MaxCarrySpeed = 1000f;
    private const float MaxCarryAngularSpeed = 25f;
    private const float CarryBreakDistance = 64f;
    private const float CarryBreakTime = 0.4f;

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

    // The pressure controller: how yielding the chase currently is (1 free, easing toward 0 while
    // blocked), its per-tick response, its floor, and the last step it asked for, which is what
    // blockage is measured against
    private const float PressureResponse = 0.35f;
    private const float MinCarryPressure = 0.1f;
    private float carryPressure = 1f;
    private Vector3 lastSteeredFrom;
    private Vector3 lastSteeredTo;

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
    /// The +USE carry: the picked-up body is held rigidly at a pose in front of the eyes, the way
    /// Half-Life 2's shadow controller pins what the player holds. Rigid here means the velocity
    /// set each tick covers the whole remaining error, so the body lands on the hold pose within
    /// the tick; but because it is still a velocity, the solver keeps the last word, and a prop
    /// slammed into a wall stops at the wall. The tracking velocity is also what the prop leaves
    /// with: dropping mid-stride keeps the player's motion, and a flick of the view throws it.
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
        var (holdPosition, holdRotation) = prop.ComputeCarryPose(EntitySystem.CurrentTime);

        // A prop that stays far from its hold pose is wedged somewhere it cannot leave; the
        // engine drops what it cannot keep hold of, but only after a strain the flick of a
        // throw never sustains
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

        // How much of last tick's step the solver denied: a free body completes its step exactly,
        // however fast the view spins, so anything missing was taken by a contact
        var attempted = MathF.Max(Vector3.Distance(lastSteeredFrom, lastSteeredTo), 1f);
        var blockedFraction = Math.Clamp(Vector3.Distance(body.Position, lastSteeredTo) / attempted, 0f, 1f);

        // Pressing on something eases the chase off, over a few ticks either way, so a prop with
        // nowhere to go rests against the obstacle instead of grinding on it at full chase speed -
        // and picks itself back up the moment the way is clear
        carryPressure = float.Lerp(carryPressure, 1f - blockedFraction, PressureResponse);
        var pressure = MathF.Max(carryPressure, MinCarryPressure);

        // The shadow controller move: chase velocities toward the hold pose, capped. The cap is
        // what lets contacts win - a blocked prop presses with a bounded speed instead of being
        // handed its whole error as fresh approach velocity every tick, which the solver would
        // fight forever and the hit events would replay as an impact each time.
        var velocity = (holdPosition - body.Position) / tickInterval;
        var speed = velocity.Length();
        var speedCap = MaxCarrySpeed * pressure;

        if (speed > speedCap)
        {
            velocity *= speedCap / speed;
        }

        var angularVelocity = RotationError(body.Rotation, holdRotation) / tickInterval;
        var angularSpeed = angularVelocity.Length();
        var angularCap = MaxCarryAngularSpeed * pressure;

        if (angularSpeed > angularCap)
        {
            angularVelocity *= angularCap / angularSpeed;
        }

        body.LinearVelocity = velocity;
        body.AngularVelocity = angularVelocity;

        lastSteeredFrom = body.Position;
        lastSteeredTo = body.Position + velocity * tickInterval;
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
        carryPressure = 1f;
        lastSteeredFrom = prop.Body.Position;
        lastSteeredTo = prop.Body.Position;
        prop.BeginCarry(this, HoldDistance + Vector3.Distance(hit.Point, prop.Body.CenterOfMass));
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
