namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>worldspawn</c>, Source's <c>CWorld</c>: the root of the entity hierarchy. Every world has exactly
/// one, from the keyvalues of the map that supplies the entity world's root.
/// </summary>
public sealed class WorldEntity : BaseEntity
{
    /// <summary>Initializes the world from the map's authored worldspawn keyvalues.</summary>
    public WorldEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Draws nothing; the world geometry already draws itself.</summary>
    protected override SceneNode? CreateRootNode() => null;
}
