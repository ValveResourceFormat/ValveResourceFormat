namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_light_probe_volume</c>, and <c>env_combined_light_probe_volume</c>, which also bakes a cubemap.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEnvLightProbeVolume">CEnvLightProbeVolume</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEnvCombinedLightProbeVolume">CEnvCombinedLightProbeVolume</seealso>
public sealed class EnvLightProbeVolume : EnvLightingVolume
{
    private readonly bool bakesCubemap;

    /// <summary>Initializes a light probe volume from its keyvalues.</summary>
    /// <param name="system">The entity system the volume belongs to.</param>
    /// <param name="spawnInfo">The volume's keyvalues and placement.</param>
    /// <param name="bakesCubemap">Whether it is an <c>env_combined_light_probe_volume</c>.</param>
    public EnvLightProbeVolume(EntitySystem system, EntitySpawnInfo spawnInfo, bool bakesCubemap) : base(system, spawnInfo, isSphere: false)
    {
        this.bakesCubemap = bakesCubemap;
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        if (bakesCubemap)
        {
            AddEnvironmentMap();
        }

        AddLightProbe();
    }
}
