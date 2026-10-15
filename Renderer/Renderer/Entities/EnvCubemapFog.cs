using System.Linq;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_cubemap_fog</c>: fog coloured from a cubemap texture, a sky material, or the material of a
/// named <c>env_sky</c>.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEnvCubemapFog">CEnvCubemapFog</seealso>
public sealed class EnvCubemapFog : BaseEntity
{
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

        Fog = SceneCubemapFog.FromEntityProperties(Scene, entity, Transform, FindSky);

        if (Fog != null)
        {
            Scene.FogInfo.CubemapFog = Fog;
            Scene.FogInfo.CubeFogActive = Fog.CubemapFogTexture != null;
        }
    }

    private SceneCubemapFog.SkySource? FindSky(string targetName)
        => EntitySystem.FindAllByTargetName(targetName).OfType<EnvSky>().FirstOrDefault() is { } sky
            ? new SceneCubemapFog.SkySource(sky.SkyMaterialName, sky.Transform, sky.BrightnessScale)
            : null;
}
