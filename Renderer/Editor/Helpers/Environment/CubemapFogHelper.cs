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
    /// <summary>Sets this fog as the cubemap fog of the entity scene. It is drawn only while CubemapFogTexture is set.</summary>
    public void Apply()
    {
        var fog = CreateFog();
        var fogInfo = Owner.Scene.FogInfo;

        fogInfo.CubemapFog = fog;
        fogInfo.CubeFogActive = fog.CubemapFogTexture != null;
    }

    private SceneCubemapFog CreateFog()
    {
        var source = (SceneCubemapFog.FogSource)GetIntegerKey("cubemapfogsource");
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
                // The first entity with that name is the sky. The fog takes its transform.
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

    // Older entities have a height fog flag, newer ones a height type. When cubemapfogheightend is set,
    // the height width is the end height minus the start height.
    private void SetHeight(SceneCubemapFog fog)
    {
        var heightType = GetIntegerKey("cubemapfogheighttype", -1);
        var hasHeightFog = heightType < 0 ? GetBooleanKey("cubemapheightfog", true) : heightType == 1;
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
