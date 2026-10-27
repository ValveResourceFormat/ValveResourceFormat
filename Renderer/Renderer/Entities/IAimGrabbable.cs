namespace ValveResourceFormat.Renderer.Entities;

/// <summary>An entity a held use drives from the aim, such as a lever or crank following the view.</summary>
public interface IAimGrabbable
{
    /// <summary>Finds how far along the aim the entity can be taken hold of, within <paramref name="reach"/>.</summary>
    bool TryAim(Vector3 eyePosition, Vector3 forward, float reach, out float distance);

    /// <summary>Runs when a held use takes hold at <paramref name="distance"/> along the aim.</summary>
    void BeginAimGrab(PlayerEntity player, Vector3 eyePosition, Vector3 forward, float distance);

    /// <summary>Follows the aim while the use stays held.</summary>
    void UpdateAimGrab(Vector3 eyePosition, Vector3 forward, float deltaTime);

    /// <summary>Runs when the use is let go.</summary>
    void EndAimGrab();
}
