using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The fog of an <c>env_cubemap_fog</c>, colored from a cubemap, a sky material, or the sky of the sky
/// entity it names.
/// </summary>
public sealed class CubemapFogHelper : FogHelper
{
    /// <summary>Initializes the fog of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public CubemapFogHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override void Apply()
    {
        var fogInfo = Owner.Scene.FogInfo;

        if (SceneCubemapFog.FromEntityProperties(Owner.Scene, Owner.KeyValues, Owner.Transform, FindSky) is not { } fog)
        {
            return;
        }

        fogInfo.CubemapFog = fog;
        fogInfo.CubeFogActive = fog.CubemapFogTexture != null;
    }

    private SceneCubemapFog.SkySource? FindSky(string targetName)
    {
        foreach (var sky in Owner.FindByName(targetName))
        {
            if (sky.Classname is "env_sky" or "env_global_light")
            {
                return new SceneCubemapFog.SkySource(SkyboxHelper.GetMaterialName(sky.KeyValues), sky.Transform, SkyboxHelper.GetBrightnessScale(sky.KeyValues));
            }
        }

        return null;
    }
}
