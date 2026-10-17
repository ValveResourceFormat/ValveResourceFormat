using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The fog of an <c>env_fog_controller</c>: a color faded in from a start to an end distance, up to a
/// maximum density, which the map has while the entity is the <c>fog</c> global.
/// </summary>
internal sealed class FogHelper(IHelperOwner owner, FgdHelper declaration) : EntityHelper(owner, declaration), ILinearFogSource
{
    /// <inheritdoc/>
    public LinearFog GetFog()
    {
        var isEnabled = LeadingNumber.ParseInteger(GetStringKey("fogenable")) != 0;
        var color = EnvironmentKeys.ParseColor(GetStringKey("fogcolor")).ToLinearColor().AsVector3();

        // A fog that is off covers nothing, though it keeps its color
        return new LinearFog(
            color * GetFloatKey("hdrcolorscale"),
            GetFloatKey("fogstart"),
            GetFloatKey("fogend"),
            isEnabled ? Math.Clamp(GetFloatKey("fogmaxdensity"), 0f, 1f) : 0f,
            GetFloatKey("fogexponent"));
    }
}
