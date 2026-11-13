using System.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The sky of an <c>env_sky</c>, drawn behind everything else of the map, tinted and fogged as the entity
/// says.
/// </summary>
internal sealed class SkyboxHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    // Where the angular fog fades below and above the horizon, while the entity does not say
    private static readonly Vector4 DefaultAngularFog = new(-25f, -35f, 25f, 35f);

    /// <summary>Gets the material of the sky, or <see langword="null"/> when the entity names no material.</summary>
    private string? MaterialName => GetStringKey("skyname") is { Length: > 0 } name
        && Path.GetExtension(name).Equals(".vmat", StringComparison.OrdinalIgnoreCase) ? name : null;

    /// <summary>Gets the brightness scale of the sky from the entity.</summary>
    private float BrightnessScale => GetFloatKey("brightnessscale", 1f);

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (MaterialName is not { } materialName || GetBooleanKey("startdisabled"))
        {
            return;
        }

        var tint = ValueParsing.TryParseColor(GetStringKey("tint_color"), out var tintColor) ? tintColor.ToLinearColor().AsVector3() : Vector3.One;

        // The sky is infinitely far away, so only the rotation of the entity applies
        var transform = Owner.RigidTransform with { Translation = Vector3.Zero };
        var fogType = (SkyFogType)GetIntegerKey("fog_type", (int)SkyFogType.Distance);
        var angularFog = new Vector4(
            GetFloatKey("angular_fog_min_start", DefaultAngularFog.X),
            GetFloatKey("angular_fog_min_end", DefaultAngularFog.Y),
            GetFloatKey("angular_fog_max_start", DefaultAngularFog.Z),
            GetFloatKey("angular_fog_max_end", DefaultAngularFog.W));

        SceneSkybox2D.Replace(Owner.Scene, materialName, tint * BrightnessScale, transform, fogType, angularFog);
    }
}
