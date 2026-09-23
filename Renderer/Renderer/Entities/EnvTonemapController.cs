using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_tonemap_controller</c>. Overrides the exposure the post processing volumes set. The last one
/// marked <c>master</c> is the one that applies, or the first one spawned when none is.
/// </summary>
public sealed class EnvTonemapController : BaseEntity
{
    /// <summary>Gets the controller built from this entity's keyvalues.</summary>
    public SceneTonemapController? Controller { get; private set; }

    /// <summary>Initializes an <c>env_tonemap_controller</c> from its keyvalues.</summary>
    public EnvTonemapController(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var minExposure = KeyValues.GetFloatProperty("minexposure");
        var maxExposure = KeyValues.GetFloatProperty("maxexposure");
        var exposureRate = KeyValues.GetFloatProperty("rate");

        Controller = new SceneTonemapController(Scene)
        {
            ControllerExposureSettings = new ExposureSettings()
            {
                ExposureMin = minExposure,
                ExposureMax = maxExposure,
                ExposureSpeedDown = exposureRate,
                ExposureSpeedUp = exposureRate,
            },
        };

        Scene.PostProcessInfo.AddTonemapController(Controller, KeyValues.GetBooleanProperty("master"));
    }
}
