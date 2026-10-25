using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_particle_glow</c>: a particle system whose effect reads its tint from control point 16, and its
/// alpha, scale and self-illumination scale from control point 17.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CEnvParticleGlow">CEnvParticleGlow</seealso>
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

        if (Effect != null)
        {
            SetGlowControlPoints(Effect, KeyValues);
        }
    }

    /// <summary>Sets the tint and the scales of a glow effect, and the texture it is drawn with, as the keys give them.</summary>
    /// <param name="effect">The effect.</param>
    /// <param name="keyValues">The keyvalues of the entity.</param>
    internal static void SetGlowControlPoints(ParticleSceneNode effect, EntityLump.Entity keyValues)
    {
        effect.GetControlPoint(16).Position = keyValues.GetVector3Property("colortint", new Vector3(255f));
        effect.GetControlPoint(17).Position = new Vector3(
            keyValues.GetFloatProperty("alphascale", 1f),
            keyValues.GetFloatProperty("scale", 1f),
            keyValues.GetFloatProperty("selfillumscale", 1f));

        var textureOverride = keyValues.GetStringProperty("effect_textureOverride");

        if (!string.IsNullOrEmpty(textureOverride))
        {
            effect.SetTextureOverride(textureOverride);
        }
    }

    [EntityInput("SetAlphaScale")]
    private void InputSetAlphaScale(EntityInputData data) => SetGlowParameter(0, data.Float());

    [EntityInput("SetScale")]
    private void InputSetScale(EntityInputData data) => SetGlowParameter(1, data.Float());

    [EntityInput("SetColorTint")]
    private void InputSetColorTint(EntityInputData data)
    {
        if (Effect != null)
        {
            Effect.GetControlPoint(16).Position = data.Vector();
        }
    }

    private void SetGlowParameter(int component, float value)
    {
        if (Effect == null)
        {
            return;
        }

        var controlPoint = Effect.GetControlPoint(17);
        var parameters = controlPoint.Position;
        parameters[component] = value;
        controlPoint.Position = parameters;
    }
}
