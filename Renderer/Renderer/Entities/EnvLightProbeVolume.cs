namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_light_probe_volume</c>, and <c>env_combined_light_probe_volume</c>, which bakes a cubemap
/// alongside its probes.
/// </summary>
public sealed class EnvLightProbeVolume : EnvLightingVolume
{
    /// <summary>Initializes a light probe volume from its keyvalues.</summary>
    public EnvLightProbeVolume(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        if (Classname.Equals("env_combined_light_probe_volume", StringComparison.OrdinalIgnoreCase))
        {
            AddEnvironmentMap();
        }

        AddLightProbe();
    }
}
