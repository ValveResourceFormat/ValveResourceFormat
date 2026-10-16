using System.Globalization;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Map;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveMapEntity = ValveResourceFormat.IO.ContentFormats.ValveMap.BaseEntity;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The exposure limits of an <c>env_tonemap_controller</c>, which the map is exposed within while the entity
/// is the <c>tonemap</c> global.
/// </summary>
public sealed class ToneMappingHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <summary>Initializes the exposure limits of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public ToneMappingHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

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

    // Only a key in exactly this case counts, so one of the map is looked up as the map stores it
    private float? GetExposure(string key)
    {
        if (Owner is not MapEntity { PlacedNode.Node: ValveMapEntity node })
        {
            return Owner.KeyValues.ContainsKey(key.ToLowerInvariant()) ? GetFloatKey(key.ToLowerInvariant()) : null;
        }

        foreach (var (name, value) in node.EntityProperties)
        {
            if (name.Equals(key, StringComparison.Ordinal) && value is string text)
            {
                return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var exposure) ? exposure : null;
            }
        }

        return null;
    }
}
