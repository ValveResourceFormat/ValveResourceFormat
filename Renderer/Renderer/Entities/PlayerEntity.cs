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

    /// <summary>Gets the controller whose state this entity reflects.</summary>
    public IPlayerController Controller { get; }

    // Held and changed buttons, latched once per tick
    private PlayerButtonState Buttons;

    // The kinematic body standing where the player stands, so walking into props shoves them
    private Body presenceBody;
    private bool hasPresenceBody;

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
            PressUse();
        }

        UpdatePhysicsPresence(tickInterval);
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
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();

        if (hasPresenceBody)
        {
            presenceBody.Destroy();
            hasPresenceBody = false;
        }
    }
}
