using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

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
        fogInfo.Volumetric.SetController(KeyValues, WorldOrigin);
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

        var indirectStrength = entity.GetBooleanProperty("overridefogindirectstrength")
            ? entity.GetFloatProperty("fogindirectstrength", 1f)
            : -1f;
        var tint = entity.GetBooleanProperty("overridetintcolor")
            ? ColorSpace.SrgbGammaToLinear(entity.GetColor32Property("tintcolor"))
            : (Vector3?)null;

        var shape = entity.GetInt32Property("shape");

        // Volumes without a height fade only have the box and radial shapes
        if (shape >= VolumetricFog.ShapeHeight && !entity.TryGetValue("heightfogdepth", out _))
        {
            shape = 0;
        }

        // The volumes that read their noise strength are the ones whose box turns with the entity
        var followsRotation = entity.TryGetValue("fognoisestrength", out _);

        var newVolume = new VolumetricFog.FogVolume(
            RigidTransform,
            entity.GetVector3Property("box_mins"),
            entity.GetVector3Property("box_maxs"),
            entity.GetFloatProperty("fogstrength", 1f),
            entity.GetFloatProperty("falloffexponent", 1f),
            shape,
            entity.GetFloatProperty("heightfogdepth"),
            entity.GetFloatProperty("heightfogedgewidth"),
            indirectStrength,
            tint,
            followsRotation);

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
