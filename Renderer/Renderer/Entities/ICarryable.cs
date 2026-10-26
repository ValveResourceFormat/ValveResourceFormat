using Box3D;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>An entity the player can pick up with +USE.</summary>
public interface ICarryable
{
    /// <summary>Gets whether the entity has been removed from the world.</summary>
    bool IsRemoved { get; }

    /// <summary>Gets whether the player may pick this up right now.</summary>
    bool CanBeCarried { get; }

    /// <summary>Gets the body the carry steers. Only meaningful while carried.</summary>
    Body CarryBody { get; }

    /// <summary>Starts the carry, holding the grabbed body when the entity has several.</summary>
    void BeginCarry(PlayerEntity carrier, float carryDistance, Body grabbedBody);

    /// <summary>Ends the carry, restoring whatever the grab suspended.</summary>
    void EndCarry();

    /// <summary>Computes the hold pose for this frame; a <see langword="null"/> rotation leaves orientation free.</summary>
    (Vector3 Position, Quaternion? Rotation) ComputeHoldPose(float deltaTime);
}
