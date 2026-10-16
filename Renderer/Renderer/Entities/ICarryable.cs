using Box3D;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Something the player can pick up with +USE. The carry in <see cref="PlayerEntity"/> steers one
/// rigid body toward a hold pose in front of the view; what that body is - a prop's whole body, or
/// the one ragdoll part that was grabbed - is the implementer's business.
/// </summary>
public interface ICarryable
{
    /// <summary>Gets whether the entity has been removed from the world.</summary>
    bool IsRemoved { get; }

    /// <summary>Gets whether the player may pick this up right now.</summary>
    bool CanBeCarried { get; }

    /// <summary>Gets the body the carry steers. Only meaningful while carried.</summary>
    Body CarryBody { get; }

    /// <summary>
    /// Starts the carry. The grabbed body names which part was picked when the entity simulates
    /// several; an entity with one body may ignore it.
    /// </summary>
    void BeginCarry(PlayerEntity carrier, float carryDistance, Body grabbedBody);

    /// <summary>Ends the carry, restoring whatever the grab suspended.</summary>
    void EndCarry();

    /// <summary>
    /// Computes where the carried body belongs this frame. A <see langword="null"/> rotation
    /// leaves the body's orientation unsteered: a ragdoll part is held by position alone and
    /// swings free, the rest of the ragdoll hanging off it by gravity and its joints.
    /// </summary>
    (Vector3 Position, Quaternion? Rotation) ComputeHoldPose();

    /// <summary>
    /// Relaxes the grip orientation part way toward the body's current one, for when the world is
    /// twisting the held body away from the hold rotation. Only called for a carry that steers
    /// orientation.
    /// </summary>
    void AdoptCarryRotation(float fraction)
    {
    }
}
