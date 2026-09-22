namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>env_cubemap</c>, a sphere of influence, and <c>env_cubemap_box</c>, a projected box.</summary>
public sealed class EnvCubemap : EnvLightingVolume
{
    /// <summary>Initializes a cubemap from its keyvalues.</summary>
    public EnvCubemap(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();
        AddEnvironmentMap();
    }
}
