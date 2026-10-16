using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>A helper an entity can give the fog of the map by, while it is the <c>fog</c> global.</summary>
public interface ILinearFogSource
{
    /// <summary>Gets the fog the helper gives the map.</summary>
    /// <param name="fog">The fog the map had so far, changed to the one of the helper.</param>
    /// <returns>Whether the helper gives the fog, rather than leaving it to the next helper of the entity.</returns>
    bool TryGetFog(ref LinearFog fog);
}
