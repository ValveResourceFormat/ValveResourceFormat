using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_sky</c>: sets the scene's <see cref="Scene.Skybox2D"/>, replacing an earlier one.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEnvSky">CEnvSky</seealso>
public sealed class EnvSky : BaseEntity
{
    /// <summary>Gets the sky material, or <see langword="null"/> when the entity names none.</summary>
    public string? SkyMaterialName { get; private set; }

    /// <summary>Gets the authored <c>brightnessscale</c>, or 1 when it was left unset.</summary>
    public float BrightnessScale { get; private set; } = 1f;

    /// <summary>Initializes an <c>env_sky</c> from its keyvalues.</summary>
    /// <param name="system">The entity system the sky belongs to.</param>
    /// <param name="spawnInfo">The sky's keyvalues and placement.</param>
    public EnvSky(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        SkyMaterialName = KeyValues.GetStringProperty("skyname");

        // Zero or less leaves it unset
        var brightnessScale = KeyValues.GetFloatProperty("brightnessscale", 1f);
        BrightnessScale = brightnessScale > 0f ? brightnessScale : 1f;

        // Off until an Enable input, which is not simulated. de_inferno has a disabled lighting-only sky.
        if (KeyValues.GetBooleanProperty("startdisabled") || !KeyValues.GetBooleanProperty("enabled", true))
        {
            return;
        }

        if (SkyMaterialName == null)
        {
            return;
        }

        var tint = ColorSpace.SrgbGammaToLinear(KeyValues.GetColor32Property("tint_color")) * BrightnessScale;

        var fogType = (SkyFogType)KeyValues.GetInt32Property("fog_type", (int)SkyFogType.Distance);

        // Where the angular fog fades below and above the horizon, while the entity does not say
        var angularFog = new Vector4(
            KeyValues.GetFloatProperty("angular_fog_min_start", -25f),
            KeyValues.GetFloatProperty("angular_fog_min_end", -35f),
            KeyValues.GetFloatProperty("angular_fog_max_start", 25f),
            KeyValues.GetFloatProperty("angular_fog_max_end", 35f));

        SceneSkybox2D.Replace(Scene, SkyMaterialName, tint, Transform with { Translation = Vector3.Zero }, fogType, angularFog);
    }
}
