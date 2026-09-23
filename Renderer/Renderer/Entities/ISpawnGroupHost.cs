using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Draws the spawn groups entities load and unload while the map plays, such as the stages an
/// <c>info_spawngroup_load_unload</c> swaps in. <see cref="Renderer"/> is the host of its entity system.
/// </summary>
public interface ISpawnGroupHost
{
    /// <summary>Starts drawing a spawn group whose scene is loaded and initialized.</summary>
    /// <param name="group">The group to draw.</param>
    void AddSpawnGroup(SpawnGroup group);

    /// <summary>Stops drawing a spawn group, whose entities are already gone, and releases its scene.</summary>
    /// <param name="group">The group to release.</param>
    void RemoveSpawnGroup(SpawnGroup group);
}
