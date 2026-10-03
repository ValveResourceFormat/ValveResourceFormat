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
    /// <summary>Gets whether the player is being simulated: standing in the world rather than a free camera.</summary>
    bool IsActive { get; }

    /// <summary>Gets the position of the player's feet, which is the entity origin.</summary>
    Vector3 Position { get; }

    /// <summary>Gets the current velocity in units per second.</summary>
    Vector3 Velocity { get; }

    /// <summary>Gets the half-extents of the player's collision hull, which shrink when ducking.</summary>
    Vector3 HullHalfExtents { get; }

    /// <summary>Gets where the view sits, which is what entity logic traces from.</summary>
    Vector3 EyePosition { get; }

    /// <summary>Gets the direction the player is looking.</summary>
    Vector3 ViewForward { get; }

    /// <summary>Gets the entity the player stands on, or null in the air.</summary>
    BaseEntity? GroundEntity { get; }

    /// <summary>
    /// Gets the center of the collision hull, which is where pushers move the player from. The view can
    /// trail it for a tick after a push.
    /// </summary>
    Vector3 HullCenter { get; }

    /// <summary>
    /// Sweeps the collision hull between two centers against the world and every collidable entity.
    /// </summary>
    Rubikon.TraceResult TraceHull(Vector3 startCenter, Vector3 endCenter);

    /// <summary>Gets whether the collision hull centered at a point overlaps the world or any collidable entity.</summary>
    bool IsHullStuck(Vector3 center);

    /// <summary>
    /// Moves the player by a pusher's displacement, already checked against what is in the way. The hull
    /// moves at once, as the engine moves it on the tick; the view follows over the tick interval, the
    /// way the pusher is drawn moving.
    /// </summary>
    void Push(Vector3 delta);

    /// <summary>
    /// Takes the buttons seen since the last call, reporting them against the state the previous call
    /// left. Called once per tick, by the player entity.
    /// </summary>
    /// <returns>What is held, and what changed, for the tick collecting it.</returns>
    PlayerButtonState ConsumeButtons();

    /// <summary>
    /// Moves the player somewhere else outright, keeping their velocity. Null angles keep the current ones.
    /// </summary>
    void Teleport(Vector3 feetPosition, Vector3? angles);
}
