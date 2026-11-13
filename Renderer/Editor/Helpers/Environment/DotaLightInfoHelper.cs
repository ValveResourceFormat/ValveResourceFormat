using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The day or night lighting preview of a <c>dota_light_info</c>. While the entity is the <c>fog</c> global,
/// the map has the fog color and distances of the previewed time of day.
/// </summary>
internal sealed class DotaLightInfoHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration), ILinearFogSource
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public LinearFog GetFog()
    {
        var isInspectorView = GetIntegerKey("preview_inspector_view") != 0;
        var isNight = GetIntegerKey("preview_day_or_night") != 0;
        var time = isNight ? "night" : "day";
        var colorKey = isInspectorView ? $"inspector_view_fog_color_{time}" : $"fog_color_{time}";

        // Only the color and the distances are set, the rest of the fog keeps the values of LinearFog.None
        var fog = LinearFog.None;

        if (ValueParsing.TryParseColor(GetStringKey(colorKey), out var color))
        {
            fog = fog with { Color = color.ToLinearColor().AsVector3() };
        }

        // The inspector view uses fixed fog distances
        return isInspectorView
            ? fog with { Start = 1200f, End = 4000f }
            : fog with { Start = GetFloatKey($"fog_start_{time}"), End = GetFloatKey($"fog_end_{time}") };
    }
}
