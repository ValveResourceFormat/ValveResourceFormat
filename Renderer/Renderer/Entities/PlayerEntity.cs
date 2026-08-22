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
    // The +USE carry: how far the use trace reaches, how far ahead of the mass center the base
    // hold point sits, how far behind its steering the prop may fall before the player loses hold
    // of it, and how fast it may still be moving when let go.
    private const float PickupReach = 300f;
    private const float HoldDistance = 50f;
    private const float CarryBreakDistance = 64f;
    private const float MaxReleaseSpeed = 1000f;

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

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        // Resets key latches inside player movement, if a key is presseed,
        // unpressed and pressed again in within 3 frames the tick will see it as one press.
        Buttons = Controller.ConsumeButtons();

        // The controller owns the position, so there is nothing to integrate; just keep up with it
        SyncFromController();

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
    /// The +USE carry: E picks up the prop under the crosshair, E again lets it go, and in between
    /// the body is held rigidly at a pose in front of the eyes, the way Half-Life 2's shadow
    /// controller pins what the player holds. Rigid here means the velocity set each tick covers
    /// the whole remaining error, so the body lands on the hold pose within the tick; but because
    /// it is still a velocity, the solver keeps the last word, and a prop slammed into a wall
    /// stops at the wall. The tracking velocity is also what the prop leaves with: dropping
    /// mid-stride keeps the player's motion, and a flick of the view throws it.
    /// </summary>
    private void UpdateCarry(float tickInterval)
    {
        if (Buttons.Pressed(TrackedKeys.E))
        {
            if (CarriedProp != null)
            {
                Drop();
            }
            else
            {
                TryPickup();
            }
        }

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

        // Steering sent the body somewhere last tick; where it actually got to is the measure of
        // how blocked it is. A free body lands on its target every tick however fast the view
        // spins, so this stays near zero until something real - a wall - is in the way, and a prop
        // that far behind is a prop the player has lost hold of.
        if (Vector3.Distance(body.Position, prop.LastSteeredPosition) > CarryBreakDistance)
        {
            Drop();
            return;
        }

        var (holdPosition, holdRotation) = prop.ComputeCarryPose(EntitySystem.CurrentTime);

        // The shadow controller move: velocities that land the body exactly on the target within
        // the tick, unclamped, so however fast the view spins the body keeps up and the drawing
        // never has to fall back to the physics pose. The target is what stays physical - the
        // hold pose is traced out of walls - and the solver still has the last word at contacts.
        body.MoveTowards(holdPosition, holdRotation, tickInterval, wake: true);
        prop.MarkSteeredTo(holdPosition);
    }

    private void TryPickup()
    {
        if (EntitySystem.PhysicsOrNull is not { } physics)
        {
            return;
        }

        var eyePosition = Controller.EyePosition;
        var forward = Controller.ViewForward;

        // Cast against the world too, not just props, so a crate behind a wall is not grabbable
        // through it: the wall wins the raycast and the pickup finds nothing
        var hit = physics.World.RaycastClosest(eyePosition, forward * PickupReach,
            new QueryFilter(PhysicsSimulation.PlayerCategory, PhysicsSimulation.StaticCategory | PhysicsSimulation.PropCategory));

        if (!hit.Hit || physics.GetOwner(hit.Shape.Body) is not PropPhysics prop || prop.IsRemoved || !prop.CanBeCarried)
        {
            return;
        }

        // The sandbox gravgun's hold distance: a fixed reach plus how far the grabbed surface sits
        // from the mass center, so a big crate hangs further out than a soda can and neither clips
        // the player's hull
        CarriedProp = prop;
        prop.BeginCarry(this, HoldDistance + Vector3.Distance(hit.Point, prop.Body.CenterOfMass));
    }

    private void Drop()
    {
        if (CarriedProp is { IsRemoved: false } prop)
        {
            // Lets go rather than throws: the body keeps the velocity it was carried with, which
            // is the player's own motion plus whatever a view flick added. Capped, because the
            // unclamped chase can spike for the one tick a blocked prop comes free on.
            var body = prop.Body;
            var velocity = body.LinearVelocity;
            var speed = velocity.Length();

            if (speed > MaxReleaseSpeed)
            {
                body.LinearVelocity = velocity * (MaxReleaseSpeed / speed);
            }

            prop.EndCarry();
        }

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
