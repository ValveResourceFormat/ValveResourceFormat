namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>worldspawn</c>, Source's <c>CWorld</c>: the root of the entity hierarchy. Every world has exactly
/// one - a default is created with the <see cref="EntitySystem"/>, and a map's authored worldspawn
/// replaces it when the map loads.
/// </summary>
public sealed class WorldEntity : BaseEntity
{
    internal WorldEntity(EntitySystem system) : base(system, "worldspawn")
    {
    }

    /// <summary>Initializes the world from the map's authored worldspawn keyvalues.</summary>
    public WorldEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Draws nothing; the world geometry already draws itself.</summary>
    protected override SceneNode? CreateRootNode() => null;
}
