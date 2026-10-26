using System.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The fog of an <c>env_cubemap_fog</c>, colored from a cubemap, a sky material, or the sky of the entity it
/// names. A map has the fog of the first such entity that is not disabled at the start.
/// </summary>
internal sealed class CubemapFogHelper(IHelperOwner owner, HelperDeclaration declaration) : EntityHelper(owner, declaration)
{
    /// <summary>Makes this fog the one the scene of the entity is drawn with. It is only drawn while it has a texture.</summary>
    public void Apply()
    {
        var fog = CreateFog();
        var fogInfo = Owner.Scene.FogInfo;

        fogInfo.CubemapFog = fog;
        fogInfo.CubeFogActive = fog.CubemapFogTexture != null;
    }

    private SceneCubemapFog CreateFog()
    {
        var source = (SceneCubemapFog.FogSource)EnvironmentKeys.ParseInteger(GetStringKey("cubemapfogsource"), 0);
        var transform = Owner.RigidTransform;
        var brightness = 1f;
        string? materialName = null;
        string? textureName = null;

        switch (source)
        {
            case SceneCubemapFog.FogSource.Texture:
                textureName = GetStringKey("cubemapfogtexture");
                break;

            case SceneCubemapFog.FogSource.SkyEntity:
                // Any entity of that name is the sky, which turns the fog with it
                if (Owner.FindByName(GetStringKey("cubemapfogskyentity")) is [var sky, ..])
                {
                    transform = sky.RigidTransform;
                    materialName = sky.KeyValues.GetStringProperty("skyname");
                    brightness = sky.KeyValues.GetFloatProperty("brightnessscale");
                }

                break;

            case SceneCubemapFog.FogSource.Material:
                materialName = GetStringKey("cubemapfogskymaterial");
                break;
        }

        var fog = new SceneCubemapFog(Owner.Scene)
        {
            StartDist = GetFloatKey("cubemapfogstartdistance"),
            EndDist = GetFloatKey("cubemapfogenddistance"),
            LodBias = GetFloatKey("cubemapfoglodbiase"),
            FalloffExponent = GetFloatKey("cubemapfogfalloffexponent"),
            HeightExponent = GetFloatKey("cubemapfogheightexponent"),
            Opacity = GetFloatKey("cubemapfogmaxopacity"),
            Transform = transform,
        };

        SetHeight(fog);

        if (materialName is { Length: > 0 } && Path.GetExtension(materialName).Equals(".vmat", StringComparison.OrdinalIgnoreCase))
        {
            fog.SetSkyMaterial(materialName, brightness);
        }
        else if (textureName is { Length: > 0 } && Path.GetExtension(textureName).Equals(".vtex", StringComparison.OrdinalIgnoreCase))
        {
            fog.CubemapFogTexture = Owner.Scene.RendererContext.MaterialLoader.GetTexture(textureName);
        }

        return fog;
    }

    // An older entity says whether it has height fog, a newer one says how the height fades. Where it gives
    // an end height, how high the fog reaches is the way from its start to its end.
    private void SetHeight(SceneCubemapFog fog)
    {
        var heightType = EnvironmentKeys.ParseInteger(GetStringKey("cubemapfogheighttype"), -1);
        var hasHeightFog = heightType < 0 ? EnvironmentKeys.ParseInteger(GetStringKey("cubemapheightfog"), 1) != 0 : heightType == 1;
        var heightStart = GetFloatKey("cubemapfogheightstart");
        var heightWidth = 0f;

        if (hasHeightFog)
        {
            heightWidth = GetStringKey("cubemapfogheightend").Length > 0
                ? GetFloatKey("cubemapfogheightend") - heightStart
                : GetFloatKey("cubemapfogheightwidth");
        }

        fog.UseHeightFog = hasHeightFog;
        fog.HeightStart = heightStart;
        fog.HeightEnd = heightStart + heightWidth;
    }
}
