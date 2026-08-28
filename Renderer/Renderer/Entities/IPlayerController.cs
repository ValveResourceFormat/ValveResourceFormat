using ValveResourceFormat.Renderer.Input;

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
    /// Shoves the player by a world-space delta, stopped early by the static world. The pusher physics
    /// calls this on the entity tick. A ride carry is reserved and applied as motion spread over the
    /// tick interval, so riding stays smooth; a depenetrating shove is immediate, so the hull never
    /// stays inside the pusher and its faces stay plainly solid.
    /// </summary>
    /// <returns>The part of the delta the controller took.</returns>
    Vector3 Push(Vector3 delta, bool immediate = false);

    /// <summary>
    /// Gets the reserved push the controller has not walked yet. A rider's carry is computed at
    /// <see cref="Position"/> plus this, so each tick targets the exact carried trajectory and the
    /// walk-off lag cannot accumulate into drift.
    /// </summary>
    Vector3 PendingPush { get; }

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
