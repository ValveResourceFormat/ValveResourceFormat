using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_particle_glow</c>: a particle system whose effect reads its tint from control point 16, and its
/// alpha, scale and self-illumination scale from control point 17.
/// </summary>
public sealed class EnvParticleGlow : InfoParticleSystem
{
    /// <summary>Initializes a glow from its keyvalues.</summary>
    public EnvParticleGlow(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        base.Activate();

        if (Effect == null)
        {
            return;
        }

        Effect.GetControlPoint(16).Position = KeyValues.GetVector3Property("colortint", new Vector3(255f));
        Effect.GetControlPoint(17).Position = new Vector3(
            KeyValues.GetFloatProperty("alphascale", 1f),
            KeyValues.GetFloatProperty("scale", 1f),
            KeyValues.GetFloatProperty("selfillumscale", 1f));

        var textureOverride = KeyValues.GetStringProperty("effect_textureOverride");

        if (!string.IsNullOrEmpty(textureOverride))
        {
            Effect.SetTextureOverride(textureOverride);
        }
    }
}
