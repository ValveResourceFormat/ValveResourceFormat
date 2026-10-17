using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The fog of an <c>env_fog_controller</c>: a color faded in from a start to an end distance, up to a
/// maximum density, which the map has while the entity is the <c>fog</c> global.
/// </summary>
public sealed class FogHelper : EntityHelper, ILinearFogSource
{
    /// <summary>Initializes the fog of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public FogHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public bool TryGetFog(ref LinearFog fog)
    {
        var isEnabled = LeadingNumber.ParseInteger(GetStringKey("fogenable")) != 0;
        var color = EnvironmentKeys.ParseColor(GetStringKey("fogcolor")).ToLinearColor().AsVector3();

        // A fog that is off covers nothing, though it keeps its color
        fog = new LinearFog(
            color * GetFloatKey("hdrcolorscale"),
            GetFloatKey("fogstart"),
            GetFloatKey("fogend"),
            isEnabled ? Math.Clamp(GetFloatKey("fogmaxdensity"), 0f, 1f) : 0f,
            GetFloatKey("fogexponent"));

        return true;
    }
}
