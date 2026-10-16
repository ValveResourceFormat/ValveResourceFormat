using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The day or night lighting of a <c>dota_light_info</c>, which it previews. While the entity is the
/// <c>fog</c> global, the map has the fog color and distances of the time of day it previews.
/// </summary>
public sealed class DotaLightInfoHelper : EntityHelper, ILinearFogSource
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <summary>Initializes the lighting of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public DotaLightInfoHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public bool TryGetFog(ref LinearFog fog)
    {
        var isInspectorView = EnvironmentKeys.ParseInteger(GetStringKey("preview_inspector_view"), 0) != 0;
        var isNight = EnvironmentKeys.ParseInteger(GetStringKey("preview_day_or_night"), 0) != 0;
        var time = isNight ? "night" : "day";
        var colorKey = isInspectorView ? $"inspector_view_fog_color_{time}" : $"fog_color_{time}";

        // Only the color and the distances change, the rest of the fog stays as it was
        if (GetStringKey(colorKey) is { Length: > 0 } color)
        {
            fog = fog with { Color = EnvironmentKeys.ParseColor(color).ToLinearColor().AsVector3() };
        }

        // The inspector view always fogs the same distances
        fog = isInspectorView
            ? fog with { Start = 1200f, End = 4000f }
            : fog with { Start = GetFloatKey($"fog_start_{time}"), End = GetFloatKey($"fog_end_{time}") };

        return true;
    }
}
