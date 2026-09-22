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
    /// <summary>Gets the sky material, or <see langword="null"/> when the entity names none.</summary>
    public string? SkyMaterialName { get; private set; }

    /// <summary>Gets whether the entity starts disabled.</summary>
    public bool IsStartDisabled { get; private set; }

    /// <summary>Gets the authored <c>brightnessscale</c>; zero or less means it was left unset.</summary>
    public float BrightnessScale { get; private set; } = 1f;

    private bool IsGlobalLight => Classname.Equals("env_global_light", StringComparison.OrdinalIgnoreCase);

    /// <summary>Initializes an <c>env_sky</c> or <c>env_global_light</c> from its keyvalues.</summary>
    public EnvSky(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        SkyMaterialName = KeyValues.GetStringProperty("skyname") ?? KeyValues.GetStringProperty("skybox_material_day");
        IsStartDisabled = KeyValues.GetBooleanProperty("startdisabled") || !KeyValues.GetBooleanProperty("enabled", true);
        BrightnessScale = KeyValues.GetFloatProperty("brightnessscale", 1.0f);

        if (IsGlobalLight && !IsStartDisabled)
        {
            AddDynamicSun();
        }

        // Off until an Enable input, which is not simulated. de_inferno has a disabled lighting-only sky.
        if (SkyMaterialName == null || IsStartDisabled)
        {
            return;
        }

        Scene.Skybox2D?.Delete();
        Scene.Skybox2D = CreateSkybox2D(SkyMaterialName);
    }

    private SceneSkybox2D CreateSkybox2D(string materialName)
    {
        var tint = Vector3.One;

        if (!IsGlobalLight)
        {
            tint = KeyValues.GetColor32Property("tint_color");

            if (BrightnessScale > 0f)
            {
                tint *= BrightnessScale;
            }
        }

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

        // Placed by the spawn group rather than by the entity, so it is added to the scene directly: a node
        // the entity owned would be moved onto the entity's own transform
        dynamicSun.PlaceAt(EntityTransformHelper.EulerAnglesToRotationMatrix(angles) * ParentTransform);
        Scene.Add(dynamicSun, false);

        Scene.LightingInfo.EnableDynamicShadows = true;
        Scene.LightingInfo.SunLightShadowCoverageScale = 4f;
    }
}
