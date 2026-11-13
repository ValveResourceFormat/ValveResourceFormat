namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// Draws the scenes that spawn groups load separately from the map scene, such as a 3D sky. It only knows scenes,
/// grouped by world group, and never the spawn groups that load them.
/// </summary>
public interface ISceneHost
{
    /// <summary>Starts drawing a scene; it must be initialized before the next frame.</summary>
    void AddScene(Scene scene);

    /// <summary>Stops drawing a scene and disposes it; the entities in it are already removed.</summary>
    void RemoveScene(Scene scene);
}
