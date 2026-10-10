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
    }

    private void SyncFromController()
    {
        WorldOrigin = Controller.Position;
        Velocity = Controller.Velocity;
    }
}
