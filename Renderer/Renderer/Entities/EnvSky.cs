using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_sky</c>, and Dota's <c>env_global_light</c>, which names the sky material too. Each sets the
/// scene's <see cref="Scene.Skybox2D"/>, replacing an earlier one. <c>env_global_light</c> also adds the
/// dynamic sun it describes.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEnvSky">CEnvSky</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/client/C_GlobalLight">C_GlobalLight</seealso>
public sealed class EnvSky : BaseEntity
{
    private readonly bool isGlobalLight;

    // Default of the r_dota_shadow_ambient_light convar, which scales the ambient lighting of the global light pass
    private const float ShadowAmbientLightScale = 1.5f;

    // Where the angular fog fades below and above the horizon, while the entity does not say
    private static readonly Vector4 DefaultAngularFog = new(-25f, -35f, 25f, 35f);

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
        base.Spawn();

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
        var tint = isGlobalLight ? Vector3.One : ColorSpace.SrgbGammaToLinear(KeyValues.GetColor32Property("tint_color")) * BrightnessScale;
        var rendererContext = Scene.RendererContext;

        using var skyMaterial = rendererContext.FileLoader.LoadFileCompiled(materialName);

        return new SceneSkybox2D(rendererContext.MaterialLoader.LoadMaterial(skyMaterial))
        {
            Tint = tint,
            Transform = Transform with
            {
                Translation = Vector3.Zero
            },
            FogType = (SkyFogType)KeyValues.GetInt32Property("fog_type", (int)SkyFogType.Distance),
            AngularFog = new Vector4(
                KeyValues.GetFloatProperty("angular_fog_min_start", DefaultAngularFog.X),
                KeyValues.GetFloatProperty("angular_fog_min_end", DefaultAngularFog.Y),
                KeyValues.GetFloatProperty("angular_fog_max_start", DefaultAngularFog.Z),
                KeyValues.GetFloatProperty("angular_fog_max_end", DefaultAngularFog.W)),
        };
    }

    private void AddDynamicSun()
    {
        var skyColor = KeyValues.GetColor32Property("ambientcolor2");
        var groundColor = KeyValues.TryGetValue("ambientcolor3", out _) ? KeyValues.GetColor32Property("ambientcolor3") : skyColor;
        var ambientAngles = KeyValues.GetVector3Property("ambientangles");

        // The sky and ground colors fade over the hemisphere, scaled by groundscale. ambientscale2 is not used by the
        // global light pass, and ambientcolor3 is the second sky color where the entity has no such key.
        // TODO: the game replaces these every frame from the ent_dota_lightinfo entities around the camera. Each one
        // weighs 1 - (distance in the XY plane - inner_radius) / (outer_radius - inner_radius), the weights are
        // multiplied by (1 - weight) of every other one and normalized, and every field is the weighted sum of its
        // *_day and *_night keys mixed by the time of day. The keys map as color, light_scale, ambient_color,
        // ambient_scale, shadow_color, shadow_secondary_color, shadow_scale, shadow_ground_scale, light_direction,
        // ambient_direction and ambient_shadow_amount to color, lightscale, ambientcolor1, ambientscale1,
        // ambientcolor2, ambientcolor3, ambientscale2, groundscale, angles, ambientangles and ambient_shadow_amount,
        // and specular_color, specular_power, specular_direction and specular_independence to specularcolor,
        // specularpower, specularangles and specularindependence.
        // dota.vmap places many overlapping lightinfos at the same origins and the client picks among them by a
        // filter that is not decoded, so the entity is used as is.
        var dynamicSun = new SceneLight(Scene)
        {
            Type = SceneLight.LightType.Directional,
            Color = KeyValues.GetColor32Property("color"),
            Brightness = KeyValues.GetFloatProperty("lightscale", 1f),
            SkyColor = skyColor,
            SkyIntensity = KeyValues.GetFloatProperty("groundscale", 1f) * ShadowAmbientLightScale,
            GroundColor = groundColor,
            AmbientDirectionColor = KeyValues.GetColor32Property("ambientcolor1"),
            AmbientDirectionIntensity = KeyValues.GetFloatProperty("ambientscale1", 1f) * ShadowAmbientLightScale,
            AmbientDirection = EntityTransformHelper.EulerAnglesToForwardDirection(ambientAngles),
            AmbientShadowAmount = KeyValues.GetFloatProperty("ambient_shadow_amount"),
            SpecularDirection = EntityTransformHelper.EulerAnglesToForwardDirection(KeyValues.GetVector3Property("specularangles")),
            SpecularColor = KeyValues.GetColor32Property("specularcolor"),
            SpecularPower = KeyValues.GetFloatProperty("specularpower", 16f),
            SpecularIndependence = KeyValues.GetFloatProperty("specularindependence"),
            LayerName = "world_layer_base",
            Name = "Source 2 Viewer dynamic sunlight for Dota",
        };

        dynamicSun.PlaceAt(Transform);
        AddNode(dynamicSun, followsEntity: false);

        // Dota fogs everything it draws with the fog controller, not only the sky
        Scene.FogInfo.ObjectsUseLinearFog = true;

        Scene.LightingInfo.EnableDynamicShadows = true;
        Scene.LightingInfo.SunLightShadowCoverageScale = 4f;
    }
}
