using System.IO;
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

    /// <summary>Gets what the brightness of the sky is scaled by, as the entity gives it.</summary>
    private float BrightnessScale => GetFloatKey("brightnessscale", 1f);

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (MaterialName is not { } materialName || EnvironmentKeys.ParseInteger(GetStringKey("startdisabled"), 0) != 0)
        {
            return;
        }

        var scene = Owner.Scene;
        var rendererContext = scene.RendererContext;

        using var skyMaterial = rendererContext.FileLoader.LoadFileCompiled(materialName);

        if (skyMaterial == null)
        {
            return;
        }

        var tintKey = GetStringKey("tint_color");
        var tint = tintKey.Length > 0 ? EnvironmentKeys.ParseColor(tintKey).ToLinearColor().AsVector3() : Vector3.One;

        // The sky is infinitely far away, so only the way the entity is turned moves it
        scene.Skybox2D?.Delete();
        scene.Skybox2D = new SceneSkybox2D(rendererContext.MaterialLoader.LoadMaterial(skyMaterial))
        {
            Tint = tint * BrightnessScale,
            Transform = Owner.RigidTransform with { Translation = Vector3.Zero },
            FogType = (SkyFogType)EnvironmentKeys.ParseInteger(GetStringKey("fog_type"), (int)SkyFogType.Distance),
            AngularFog = new Vector4(
                GetFloatKey("angular_fog_min_start", DefaultAngularFog.X),
                GetFloatKey("angular_fog_min_end", DefaultAngularFog.Y),
                GetFloatKey("angular_fog_max_start", DefaultAngularFog.Z),
                GetFloatKey("angular_fog_max_end", DefaultAngularFog.W)),
        };
    }
}
