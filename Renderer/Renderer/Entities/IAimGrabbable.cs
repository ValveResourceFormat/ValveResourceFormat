namespace ValveResourceFormat.Renderer.Entities;

/// <summary>An entity a held use drives from the aim, such as a lever or crank following the view.</summary>
public interface IAimGrabbable
{
    /// <summary>Gets the world point the aim takes hold of, or <see langword="null"/> when it cannot be grabbed now.</summary>
    Vector3? GrabPoint { get; }

    /// <summary>Runs when a held use takes hold.</summary>
    void BeginAimGrab(PlayerEntity player, Vector3 eyePosition, Vector3 forward);

    /// <summary>Follows the aim while the use stays held.</summary>
    void UpdateAimGrab(Vector3 eyePosition, Vector3 forward, float deltaTime);

    /// <summary>Runs when the use is let go.</summary>
    void EndAimGrab();
}
