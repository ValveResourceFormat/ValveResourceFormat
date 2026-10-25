using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>A helper an entity can give the fog of the map by, while it is the <c>fog</c> global.</summary>
internal interface ILinearFogSource
{
    /// <summary>Gets the fog the helper gives the map.</summary>
    LinearFog GetFog();
}
