using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>env_volumetric_fog_controller</c>. Turns on volumetric fog in its scene, and sets how it looks.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/client/C_EnvVolumetricFogController">C_EnvVolumetricFogController</seealso>
public sealed class EnvVolumetricFogController : BaseEntity
{
    /// <summary>Initializes an <c>env_volumetric_fog_controller</c> from its keyvalues.</summary>
    public EnvVolumetricFogController(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        var fogInfo = Scene.FogInfo;
        fogInfo.Volumetric ??= new VolumetricFog(Scene.RendererContext);
        fogInfo.Volumetric.SetController(KeyValues);
    }
}

/// <summary><c>env_volumetric_fog_volume</c>. A box of volumetric fog.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/hlvr/client/C_EnvVolumetricFogVolume">C_EnvVolumetricFogVolume</seealso>
public sealed class EnvVolumetricFogVolume : BaseEntity
{
    private VolumetricFog.FogVolume? fogVolume;

    /// <summary>Initializes an <c>env_volumetric_fog_volume</c> from its keyvalues.</summary>
    public EnvVolumetricFogVolume(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        var entity = KeyValues;
        var fogInfo = Scene.FogInfo;
        fogInfo.Volumetric ??= new VolumetricFog(Scene.RendererContext);
        fogInfo.Volumetric.VolumeEntityCount++;

        if (entity.GetBooleanProperty("startdisabled"))
        {
            return;
        }

        var origin = WorldOrigin;

        var newVolume = new VolumetricFog.FogVolume(
            origin + entity.GetVector3Property("box_mins"),
            origin + entity.GetVector3Property("box_maxs"),
            entity.GetFloatProperty("fogstrength", 1f),
            entity.GetFloatProperty("falloffexponent", 1f),
            entity.GetInt32Property("shape") == 1);

        fogVolume = newVolume;
        fogInfo.Volumetric.Volumes.Add(newVolume);
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        if (Scene.FogInfo.Volumetric is { } volumetric)
        {
            volumetric.VolumeEntityCount--;

            if (fogVolume is { } volume)
            {
                volumetric.Volumes.Remove(volume);
            }
        }

        base.OnRemove();
    }
}
