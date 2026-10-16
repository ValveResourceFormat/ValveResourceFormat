using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The exposure limits of an <c>env_tonemap_controller</c>.
/// </summary>
public sealed class ToneMappingHelper : EntityHelper
{
    /// <summary>Initializes the exposure limits of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public ToneMappingHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        var keyValues = Owner.KeyValues;
        var exposureRate = keyValues.GetFloatProperty("rate");

        var controller = new SceneTonemapController(Owner.Scene)
        {
            ControllerExposureSettings = new ExposureSettings()
            {
                ExposureMin = keyValues.GetFloatProperty("minexposure"),
                ExposureMax = keyValues.GetFloatProperty("maxexposure"),
                ExposureSpeedDown = exposureRate,
                ExposureSpeedUp = exposureRate,
            },
        };

        Owner.Scene.PostProcessInfo.AddTonemapController(controller, keyValues.GetBooleanProperty("master"));
    }
}
