using System.Linq;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_cubemap_fog</c>. Sets the fog of its scene that takes its colour from a cubemap: a texture, a
/// sky material, or the material of an <c>env_sky</c> it names.
/// </summary>
public sealed class EnvCubemapFog : BaseEntity
{
    /// <summary>The <c>cubemapfogsource</c> values.</summary>
    public enum FogSource : uint
    {
        /// <summary>The <c>cubemapfogtexture</c> texture. Not offered in CS2.</summary>
        Texture = 0,

        /// <summary>The sky material of the <c>env_sky</c> named by <c>cubemapfogskyentity</c>.</summary>
        SkyEntity = 1,

        /// <summary>The <c>cubemapfogskymaterial</c> material.</summary>
        Material = 2,
    }

    /// <summary>Gets the fog this entity set, or <see langword="null"/> when it did not take effect.</summary>
    public SceneCubemapFog? Fog { get; private set; }

    /// <summary>Initializes an <c>env_cubemap_fog</c> from its keyvalues.</summary>
    public EnvCubemapFog(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    // In Activate, as a sky entity source may be authored after the fog
    /// <inheritdoc/>
    public override void Activate()
    {
        var entity = KeyValues;

        // Off until an Enable input, which is not simulated
        if (entity.GetBooleanProperty("startdisabled"))
        {
            return;
        }

        var fogInfo = Scene.FogInfo;
        var rendererContext = Scene.RendererContext;
        var transform = Transform;

        var lodBias = entity.GetFloatProperty("cubemapfoglodbiase");

        var falloffExponent = entity.GetFloatProperty("cubemapfogfalloffexponent");
        var startDist = entity.GetFloatProperty("cubemapfogstartdistance");
        var endDist = entity.GetFloatProperty("cubemapfogenddistance");

        var hasHeightEnd = entity.ContainsKey("cubemapfogheightend");

        var useHeightFog = entity.ContainsKey("cubemapfogheightexponent"); // the oldest versions have these values missing, so disable it there
        useHeightFog = entity.GetBooleanProperty("cubemapheightfog", useHeightFog); // New in CS2

        var heightExponent = 1.0f;
        var heightStart = float.PositiveInfinity; // is this right?
        var heightEnd = float.PositiveInfinity;
        if (useHeightFog)
        {
            heightExponent = entity.GetFloatProperty("cubemapfogheightexponent");
            heightStart = entity.GetFloatProperty("cubemapfogheightstart");
            if (hasHeightEnd)
            {
                // New in CS2
                heightEnd = entity.GetFloatProperty("cubemapfogheightend");
            }
            else
            {
                var heightWidth = entity.GetFloatProperty("cubemapfogheightwidth");
                heightEnd = heightStart + heightWidth;
            }
        }

        var opacity = entity.GetFloatProperty("cubemapfogmaxopacity", 1f);
        var fogSource = (FogSource)entity.GetUInt32Property("cubemapfogsource");

        RenderTexture? fogTexture = null;
        var exposureBias = 0.0f;
        string? material = null;
        var skyBrightnessScale = 1.0f;

        switch (fogSource)
        {
            case FogSource.Texture:
                var textureName = entity.GetStringProperty("cubemapfogtexture");
                if (textureName != null)
                {
                    fogTexture = rendererContext.MaterialLoader.GetTexture(textureName);
                }

                break;

            case FogSource.SkyEntity:
                var skyEntTargetName = entity.GetStringProperty("cubemapfogskyentity");

                if (skyEntTargetName == null)
                {
                    break;
                }

                // Only in this entity's own spawn group: a 3D sky shares names with the map it is placed in
                if (EntitySystem.FindAllByTargetName(skyEntTargetName, Scene).OfType<EnvSky>().FirstOrDefault() is not { } sky)
                {
                    EntitySystem.Logger.LogWarning("Disabling cubemap fog because failed to find env_sky of target name {SkyEntTargetName}", skyEntTargetName);
                    return;
                }

                material = sky.SkyMaterialName;
                transform = sky.Transform with { Translation = transform.Translation }; // steal rotation from env_sky
                skyBrightnessScale = sky.BrightnessScale;
                break;

            case FogSource.Material:
                material = entity.GetStringProperty("cubemapfogskymaterial");
                break;

            default:
                EntitySystem.Logger.LogWarning("Disabling cubemap fog '{TargetName}' because its fog source {FogSource} is not recognized", TargetName, fogSource);
                return;
        }

        if (!string.IsNullOrEmpty(material))
        {
            using var matFile = rendererContext.FileLoader.LoadFileCompiled(material);
            var mat = rendererContext.MaterialLoader.LoadMaterial(matFile);

            if (mat != null && mat.Textures.TryGetValue("g_tSkyTexture", out fogTexture))
            {
                var brightnessExposureBias = mat.FloatParams.GetValueOrDefault("g_flBrightnessExposureBias", 0f);
                // todo: make sure this matches with scene post process
                var renderOnlyExposureBias = mat.FloatParams.GetValueOrDefault("g_flRenderOnlyExposureBias", 0f);

                // These are both logarithms, so this is equivalent to a multiply of the raw value
                exposureBias = brightnessExposureBias + renderOnlyExposureBias + MathF.Log2(skyBrightnessScale);
            }
        }

        Fog = new SceneCubemapFog(Scene)
        {
            StartDist = startDist,
            EndDist = endDist,
            FalloffExponent = falloffExponent,
            HeightStart = heightStart,
            HeightEnd = heightEnd,
            HeightExponent = heightExponent,
            LodBias = lodBias,
            Transform = transform,
            CubemapFogTexture = fogTexture,
            Opacity = opacity,
            ExposureBias = exposureBias,
            UseHeightFog = useHeightFog,
        };

        fogInfo.CubemapFog = Fog;
        fogInfo.CubeFogActive = fogTexture != null;
    }
}
