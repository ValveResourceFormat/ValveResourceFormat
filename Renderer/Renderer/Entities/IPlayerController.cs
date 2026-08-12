namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The physical state of a player, as much of it as the entity world needs: where they stand, how fast,
/// how big, and how to move them somewhere else.
/// </summary>
/// <remarks>
/// Declared next to its consumer so the entity world does not depend on whatever drives the player. The
/// player moves per rendered frame rather than on the entity tick, so <see cref="PlayerEntity"/> reads an
/// implementation of this instead of owning the state.
/// </remarks>
public interface IPlayerController
{
    /// <summary>Gets the position of the player's feet, which is the entity origin.</summary>
    Vector3 Position { get; }

    /// <summary>Gets the current velocity in units per second.</summary>
    Vector3 Velocity { get; }

    /// <summary>Gets the half-extents of the player's collision hull, which shrink when ducking.</summary>
    Vector3 HullHalfExtents { get; }

    /// <summary>
    /// Moves the player somewhere else outright, keeping their velocity.
    /// </summary>
    /// <param name="feetPosition">Where the feet arrive.</param>
    /// <param name="angles">View angles to adopt, or <see langword="null"/> to keep the current ones.</param>
    void Teleport(Vector3 feetPosition, Vector3? angles);
}
