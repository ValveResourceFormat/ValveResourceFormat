using System.Linq;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Dota's <c>env_global_light</c>: the dynamic sun. The game replaces the sun, ambient light, fog colors and
/// distances, and sets the sky, from the <see cref="DotaLightInfo"/> entities around the camera. The light
/// stands in for the camera, and the day values count.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/dota2/client/C_GlobalLight">C_GlobalLight</seealso>
public sealed class EnvGlobalLight : BaseEntity
{
    // Default of the r_dota_shadow_ambient_light convar, which scales the ambient lighting of the global light pass
    private const float ShadowAmbientLightScale = 1.5f;

    private SceneLight? sun;

    /// <summary>Initializes an <c>env_global_light</c> from its keyvalues.</summary>
    public EnvGlobalLight(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        // Off until an Enable input, which is not simulated
        if (KeyValues.GetBooleanProperty("startdisabled"))
        {
            return;
        }

        AddDynamicSun();
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        base.Activate();

        if (sun == null)
        {
            return;
        }

        // Every entity of the batch has spawned by now, the light infos included
        var blend = DotaLightInfo.Select(EntitySystem.Entities.OfType<DotaLightInfo>(), WorldOrigin);
        if (blend == null)
        {
            return;
        }

        blend.ApplyTo(sun, ShadowAmbientLightScale);
        blend.ApplyFog(Scene);
        blend.ApplySkybox(Scene);
    }

    private void AddDynamicSun()
    {
        var skyColor = KeyValues.GetColor32Property("ambientcolor2");
        var groundColor = skyColor;

        if (KeyValues.TryGetValue("ambientcolor3", out var ground))
        {
            var color = ground.ToColor32() ?? default;
            groundColor = new Vector3(color.R, color.G, color.B) / 255f;
        }

        var ambientAngles = KeyValues.GetVector3Property("ambientangles");

        // The sky and ground colors fade over the hemisphere, scaled by groundscale. ambientscale2 is not used by the
        // global light pass, and ambientcolor3 is the second sky color where the entity has no such key.
        sun = new SceneLight(Scene)
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
            SpecularDirection = EntityTransformHelper.EulerAnglesToForwardDirection(KeyValues.GetVector3Property("specularangles")),
            SpecularColor = KeyValues.GetColor32Property("specularcolor"),
            SpecularPower = KeyValues.GetFloatProperty("specularpower", 16f),
            SpecularIndependence = KeyValues.GetFloatProperty("specularindependence"),
            Layers = [WorldLayer.Get("world_layer_base")],
            Name = "Source 2 Viewer dynamic sunlight for Dota",
        };

        sun.PlaceAt(Transform);
        AddNode(sun, followsEntity: false);

        // Dota fogs everything it draws with the fog controller, not only the sky
        Scene.FogInfo.ObjectsUseLinearFog = true;

        Scene.LightingInfo.EnableDynamicShadows = true;
        Scene.LightingInfo.SunLightShadowCoverageScale = 4f;
    }
}
