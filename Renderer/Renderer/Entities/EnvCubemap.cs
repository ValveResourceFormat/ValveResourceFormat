namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>env_cubemap</c>, a sphere of influence, and <c>env_cubemap_box</c>, a projected box.</summary>
public sealed class EnvCubemap : EnvLightingVolume
{
    /// <summary>Initializes a cubemap from its keyvalues.</summary>
    /// <param name="system">The entity system the cubemap belongs to.</param>
    /// <param name="spawnInfo">The cubemap's keyvalues and placement.</param>
    /// <param name="isSphere">Whether it is an <c>env_cubemap</c> rather than an <c>env_cubemap_box</c>.</param>
    public EnvCubemap(EntitySystem system, EntitySpawnInfo spawnInfo, bool isSphere) : base(system, spawnInfo, isSphere)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();
        AddEnvironmentMap();
    }
}
