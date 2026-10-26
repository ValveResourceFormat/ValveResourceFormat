namespace ValveResourceFormat.Renderer.Entities;

/// <summary>An entity that reacts to a hand grabbing its body at a point, such as a physics door unlatching.</summary>
public interface IHandGrabbable
{
    /// <summary>Runs when a hand grabs the body.</summary>
    void BeginHandGrab();

    /// <summary>Runs when the hand lets go.</summary>
    void EndHandGrab();
}
