using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Draws spawn groups that entities load and unload at runtime.
/// </summary>
public interface ISpawnGroupHost
{
    /// <summary>Starts drawing a loaded spawn group.</summary>
    void AddSpawnGroup(SpawnGroup group);

    /// <summary>Stops drawing a spawn group and releases the scene; the entities are already removed.</summary>
    void RemoveSpawnGroup(SpawnGroup group);
}
