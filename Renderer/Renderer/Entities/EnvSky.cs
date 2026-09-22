using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_sky</c>, and Dota's <c>env_global_light</c>, which names the sky material too. Each sets its
/// scene's <see cref="Scene.Skybox2D"/>, a later one replacing an earlier one. An <c>env_global_light</c>
/// also adds a fixed dynamic sun.
/// </summary>
public sealed class EnvSky : BaseEntity
{
    private readonly bool isGlobalLight;

    /// <summary>Gets the sky material, or <see langword="null"/> when the entity names none.</summary>
    public string? SkyMaterialName { get; private set; }

    /// <summary>Gets the authored <c>brightnessscale</c>, or 1 when it was left unset.</summary>
    public float BrightnessScale { get; private set; } = 1f;

    /// <summary>Initializes an <c>env_sky</c> or <c>env_global_light</c> from its keyvalues.</summary>
    /// <param name="system">The entity system the sky belongs to.</param>
    /// <param name="spawnInfo">The sky's keyvalues and placement.</param>
    /// <param name="isGlobalLight">Whether it is an <c>env_global_light</c>.</param>
    public EnvSky(EntitySystem system, EntitySpawnInfo spawnInfo, bool isGlobalLight) : base(system, spawnInfo)
    {
        this.isGlobalLight = isGlobalLight;
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        SkyMaterialName = KeyValues.GetStringProperty("skyname") ?? KeyValues.GetStringProperty("skybox_material_day");

        // Zero or less leaves it unset
        var brightnessScale = KeyValues.GetFloatProperty("brightnessscale", 1f);
        BrightnessScale = brightnessScale > 0f ? brightnessScale : 1f;

        // Off until an Enable input, which is not simulated. de_inferno has a disabled lighting-only sky.
        if (KeyValues.GetBooleanProperty("startdisabled") || !KeyValues.GetBooleanProperty("enabled", true))
        {
            return;
        }

        if (isGlobalLight)
        {
            AddDynamicSun();
        }

        if (SkyMaterialName == null)
        {
            return;
        }

        Scene.Skybox2D?.Delete();
        Scene.Skybox2D = CreateSkybox2D(SkyMaterialName);
    }

    private SceneSkybox2D CreateSkybox2D(string materialName)
    {
        var tint = isGlobalLight ? Vector3.One : KeyValues.GetColor32Property("tint_color") * BrightnessScale;
        var rendererContext = Scene.RendererContext;

        using var skyMaterial = rendererContext.FileLoader.LoadFileCompiled(materialName);

        return new SceneSkybox2D(rendererContext.MaterialLoader.LoadMaterial(skyMaterial))
        {
            Tint = tint,
            Transform = Transform with
            {
                Translation = Vector3.Zero
            },
        };
    }

    private void AddDynamicSun()
    {
        var angles = new Vector3(50, 43, 0);
        var dynamicSun = new SceneLight(Scene)
        {
            Type = SceneLight.LightType.Directional,
            Color = new Vector3(1.0f, 1.0f, 1.0f),
            Brightness = 1.0f,
            LayerName = "world_layer_base",
            Name = "Source 2 Viewer dynamic sunlight for Dota",
        };

        // Fixed in the spawn group rather than at the entity
        dynamicSun.PlaceAt(EntityTransformHelper.EulerAnglesToRotationMatrix(angles) * ParentTransform);
        AddNode(dynamicSun, followsEntity: false);

        Scene.LightingInfo.EnableDynamicShadows = true;
        Scene.LightingInfo.SunLightShadowCoverageScale = 4f;
    }
}
