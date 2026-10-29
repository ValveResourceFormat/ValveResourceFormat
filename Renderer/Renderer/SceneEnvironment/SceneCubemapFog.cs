using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// Scene node representing cubemap-based volumetric fog.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEnvCubemapFog">CEnvCubemapFog</seealso>
public class SceneCubemapFog(Scene scene) : SceneNode(scene)
{
    /// <summary>Gets or sets the distance at which fog begins.</summary>
    public float StartDist { get; set; }

    /// <summary>Gets or sets the distance at which fog reaches full density.</summary>
    public float EndDist { get; set; }

    /// <summary>Gets or sets the exponent controlling the fog density falloff curve.</summary>
    public float FalloffExponent { get; set; }

    /// <summary>Gets or sets the world-space height at which height fog is at full density and begins to fade.</summary>
    public float HeightStart { get; set; }

    /// <summary>Gets or sets the world-space height at which height fog fades to zero density.</summary>
    public float HeightEnd { get; set; }

    /// <summary>Gets or sets the exponent controlling the height fog falloff curve.</summary>
    public float HeightExponent { get; set; }

    /// <summary>Gets or sets the mip LOD bias applied when sampling the cubemap fog texture.</summary>
    public float LodBias { get; set; }

    /// <summary>Gets or sets the overall opacity of the fog.</summary>
    public float Opacity { get; set; }

    /// <summary>Gets or sets whether height-based fog is enabled.</summary>
    public bool UseHeightFog { get; set; }

    /// <summary>Gets or sets the cubemap texture used for fog color sampling.</summary>
    public RenderTexture? CubemapFogTexture { get; set; }

    /// <summary>Gets or sets the exposure bias applied to the fog color.</summary>
    public float ExposureBias { get; set; }

    /// <summary>
    /// Returns a <see cref="Vector4"/> encoding the distance fog offset, scale, LOD bias, and falloff exponent for shader use.
    /// </summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector4 OffsetScaleBiasExponent(FogSpace fogSpace)
    {
        var startDist = fogSpace.Distance(StartDist);

        var scale = 1f / (fogSpace.Distance(EndDist) - startDist);
        var offset = -(startDist * scale);

        return new Vector4(offset, scale, LodBias, FalloffExponent);
    }

    // HeightWidth is equal to HeightEnd - HeightStart
    // Height width ADDS to heightStart
    /// <summary>
    /// Returns a <see cref="Vector4"/> encoding the height fog offset, scale, exponent, and log2 mip level for shader use.
    /// </summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector4 Height_OffsetScaleExponentLog2Mip(FogSpace fogSpace)
    {
        Debug.Assert(CubemapFogTexture != null);

        var offset = 1f;
        var scale = 0f;
        var exponent = 0f;

        if (HasHeightRange)
        {
            var heightStart = fogSpace.Height(HeightStart);

            scale = 1f / (heightStart - fogSpace.Height(HeightEnd));
            offset = 1f - (heightStart * scale);
            exponent = HeightExponent;
        }

        // The fog blurs down to the mip three levels above a single texel
        return new Vector4(offset, scale, exponent, MathF.Log2(CubemapFogTexture.Width) - 3f);
    }

    /// <summary>
    /// Returns a <see cref="Vector4"/> encoding the distance culling squared, height culling, linear exposure, and opacity for shader use.
    /// </summary>
    /// <param name="fogSpace">Converts authored distances and heights into the view's space.</param>
    public Vector4 CullingParams_Opacity(FogSpace fogSpace)
    {
        var startDist = fogSpace.Distance(StartDist);
        var distCull = startDist * startDist;
        var heightCull = float.PositiveInfinity;

        if (IsHalfLifeAlyx)
        {
            if (UseHeightFog || HasHeightRange)
            {
                heightCull = fogSpace.Height(HeightStart);
            }
        }
        else if (HasHeightRange)
        {
            heightCull = fogSpace.Height(HeightEnd);
        }

        return new Vector4(distCull, heightCull, MathF.Pow(2f, ExposureBias), Opacity);
    }

    private bool IsHalfLifeAlyx => Scene.RendererContext.FileLoader.GameInfo.Game == Game.Hlvr;

    // Without height fog both heights are infinite, so they compare equal. Half-Life: Alyx takes only upward ranges.
    private bool HasHeightRange => IsHalfLifeAlyx ? HeightEnd - HeightStart > 0 : HeightEnd != HeightStart;

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

    /// <summary>The sky a fog takes its color from.</summary>
    /// <param name="MaterialName">Sky material of the sky entity.</param>
    /// <param name="Transform">Where the sky entity is placed. The fog takes its rotation.</param>
    /// <param name="BrightnessScale">Brightness scale of the sky entity.</param>
    public readonly record struct SkySource(string? MaterialName, Matrix4x4 Transform, float BrightnessScale);

    /// <summary>Creates the fog an <c>env_cubemap_fog</c> entity describes.</summary>
    /// <param name="scene">The scene the fog is for.</param>
    /// <param name="entity">The keyvalues of the entity.</param>
    /// <param name="transform">Where the entity is placed.</param>
    /// <param name="findSky">Finds the sky entity with a name, for a fog that takes its color from one.</param>
    /// <returns>The fog, or <see langword="null"/> when what it takes its color from cannot be told.</returns>
    public static SceneCubemapFog? FromEntityProperties(Scene scene, EntityLump.Entity entity, Matrix4x4 transform, Func<string, SkySource?> findSky)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(findSky);

        var rendererContext = scene.RendererContext;

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

                if (findSky(skyEntTargetName) is not { } sky)
                {
                    rendererContext.Logger.LogWarning("Disabling cubemap fog because failed to find env_sky of target name {SkyEntTargetName}", skyEntTargetName);
                    return null;
                }

                material = sky.MaterialName;
                transform = sky.Transform with { Translation = transform.Translation }; // steal rotation from env_sky
                skyBrightnessScale = sky.BrightnessScale;
                break;

            case FogSource.Material:
                material = entity.GetStringProperty("cubemapfogskymaterial");
                break;

            default:
                rendererContext.Logger.LogWarning("Disabling cubemap fog '{TargetName}' because its fog source {FogSource} is not recognized", entity.GetStringProperty("targetname"), fogSource);
                return null;
        }

        var fog = new SceneCubemapFog(scene)
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
            UseHeightFog = useHeightFog,
        };

        if (!string.IsNullOrEmpty(material))
        {
            fog.SetSkyMaterial(material, skyBrightnessScale);
        }

        return fog;
    }

    /// <summary>
    /// Colors the fog from the sky texture of a sky material, exposed as the material and the sky entity
    /// make it. Leaves the fog as it is when the material does not load or has no sky texture.
    /// </summary>
    /// <param name="materialName">The sky material.</param>
    /// <param name="brightnessScale">Brightness scale of the sky entity, or 1 for a material the fog names itself.</param>
    public void SetSkyMaterial(string materialName, float brightnessScale)
    {
        var rendererContext = Scene.RendererContext;

        using var materialFile = rendererContext.FileLoader.LoadFileCompiled(materialName);

        if (materialFile == null)
        {
            return;
        }

        var material = rendererContext.MaterialLoader.LoadMaterial(materialFile);

        if (!material.Textures.TryGetValue("g_tSkyTexture", out var texture))
        {
            return;
        }

        // The bias is a power of two, which the brightness of the sky entity scales. The render only bias
        // is for drawing the sky itself, except in CS2, where the fog is as bright as the sky is drawn.
        var exposureBias = material.FloatParams.GetValueOrDefault("g_flBrightnessExposureBias");

        if (rendererContext.FileLoader.GameInfo.Game == Game.Csgo)
        {
            exposureBias += material.FloatParams.GetValueOrDefault("g_flRenderOnlyExposureBias");
        }

        CubemapFogTexture = texture;
        ExposureBias = exposureBias + MathF.Log2(brightnessScale);
    }
}
