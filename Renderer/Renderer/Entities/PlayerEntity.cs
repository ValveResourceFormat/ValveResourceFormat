using ValveResourceFormat.Renderer.Input;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The player, as an entity the rest of the world can see. Source's <c>CBasePlayer</c> is an entity like
/// any other; here the movement itself still lives in <see cref="PlayerMovement"/>, and this mirrors it
/// into the entity world so triggers have something to touch and teleports have something to move.
/// </summary>
/// <remarks>
/// Position is read from the movement rather than simulated: the player moves per rendered frame, off the
/// input, not on the entity tick. That makes this entity a view onto the movement state, which is why
/// <see cref="TryGetTouchBounds"/> reads the hull live instead of trusting the last tick's copy.
/// </remarks>
public sealed class PlayerEntity : BaseEntity
{
    /// <summary>Gets the movement this entity reflects.</summary>
    public PlayerMovement Movement { get; }

    /// <summary>
    /// Creates the player entity for a movement controller.
    /// </summary>
    /// <param name="system">The world the player belongs to.</param>
    /// <param name="movement">The movement to mirror.</param>
    public PlayerEntity(EntitySystem system, PlayerMovement movement) : base(system, "player")
    {
        Movement = movement;

        // Nothing traces against the player, and the player is what enters triggers rather than a volume
        // anything can enter.
        IsSolid = false;
    }

    /// <inheritdoc/>
    public override bool TryGetTouchBounds(out Vector3 center, out Vector3 halfExtents)
    {
        halfExtents = Movement.HullHalfExtents;
        center = Movement.Position + new Vector3(0, 0, halfExtents.Z);
        return true;
    }

    /// <summary>
    /// Teleports the player. <see cref="BaseEntity.Origin"/> is the feet, which is what
    /// <see cref="PlayerMovement.Teleport"/> takes, so the destination passes straight through.
    /// </summary>
    /// <param name="origin">Where the feet arrive.</param>
    /// <param name="angles">View angles to adopt, or <see langword="null"/> to keep the current ones.</param>
    public override void Teleport(Vector3 origin, Vector3? angles)
    {
        Movement.Teleport(origin, angles);
        SyncFromMovement();
    }

    /// <inheritdoc/>
    protected override void PhysicsSimulate(float tickInterval)
    {
        // The movement owns the position, so there is nothing to integrate; just keep up with it
        SyncFromMovement();
    }

    private void SyncFromMovement()
    {
        Origin = Movement.Position;
        Velocity = Movement.Velocity;
    }
}
