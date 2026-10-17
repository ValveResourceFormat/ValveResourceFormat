using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The distance and height fog of an <c>env_gradient_fog</c>, which the map has while the entity is the
/// <c>gradient_fog</c> global.
/// </summary>
internal sealed class GradientFogHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <summary>
    /// Gets whether the fog is only there while the entity names a gradient fog texture, as the block the
    /// helper is declared with can ask.
    /// </summary>
    private bool IsTextureRequired => GetMetadataFlag("textureRequired", false);

    /// <summary>Gets the gradient fog texture the entity names, or <see langword="null"/> when it names none.</summary>
    private string? TextureName => GetStringKey("gradientfogtexture") is { Length: > 0 } name ? name : null;

    /// <summary>Makes this fog the one the scene of the entity is drawn with, unless it lacks the texture it requires.</summary>
    public void Apply()
    {
        if (IsTextureRequired && TextureName == null)
        {
            return;
        }

        var fogInfo = Owner.Scene.FogInfo;

        fogInfo.GradientFogActive = true;
        fogInfo.GradientFog = CreateFog();
    }

    private SceneGradientFog CreateFog()
    {
        var hasHeightFog = EnvironmentKeys.ParseInteger(GetStringKey("heightfog"), 1) != 0;
        var strength = GetFloatKey("fogstrength");

        // The fog color defaults to white where the entity has none
        var colorKey = GetStringKey("fogcolor");
        var color = colorKey.Length > 0 ? EnvironmentKeys.ParseColor(colorKey) : Color32.White;

        // Without height fog the fog has no height range, and its height does not thin it
        return new SceneGradientFog(Owner.Scene)
        {
            StartDist = GetFloatKey("fogstart"),
            EndDist = GetFloatKey("fogend"),
            HeightStart = hasHeightFog ? GetFloatKey("fogstartheight") : 0f,
            HeightEnd = hasHeightFog ? GetFloatKey("fogendheight") : 0f,
            MaxOpacity = GetFloatKey("fogmaxopacity"),
            FalloffExponent = GetFloatKey("fogfalloffexponent"),
            VerticalExponent = hasHeightFog ? GetFloatKey("fogverticalexponent") : 0f,
            Color = color.ToLinearColor().AsVector3(),
            Strength = strength,
        };
    }
}
