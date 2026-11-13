using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The exposure limits of an <c>env_tonemap_controller</c>. The map is exposed within them while the entity
/// is the <c>tonemap</c> global.
/// </summary>
internal sealed class ToneMappingHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <summary>Sets these limits as the exposure limits of the entity scene. They take precedence over the limits of any post processing.</summary>
    public void Apply()
    {
        var defaults = new ExposureSettings();

        var controller = new SceneTonemapController(Owner.Scene)
        {
            ControllerExposureSettings = defaults with
            {
                AutoExposureEnabled = true,
                ExposureMin = GetExposure("MinExposure") ?? defaults.ExposureMin,
                ExposureMax = GetExposure("MaxExposure") ?? defaults.ExposureMax,
            },
        };

        Owner.Scene.PostProcessInfo.AddTonemapController(controller, isMaster: true);
    }

    private float? GetExposure(string key)
        => Owner.KeyValues.GetNullableFloatProperty(key.ToLowerInvariant());
}
