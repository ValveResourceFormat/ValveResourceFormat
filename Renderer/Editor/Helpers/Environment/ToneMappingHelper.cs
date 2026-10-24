using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The exposure limits of an <c>env_tonemap_controller</c>, which the map is exposed within while the entity
/// is the <c>tonemap</c> global.
/// </summary>
internal sealed class ToneMappingHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <summary>Makes these limits the ones the scene of the entity is exposed within, over those of any post processing.</summary>
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
        => Owner.KeyValues.ContainsKey(key.ToLowerInvariant()) ? GetFloatKey(key.ToLowerInvariant()) : null;
}
