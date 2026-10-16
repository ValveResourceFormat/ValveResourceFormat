using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The glow of an <c>env_particle_glow</c>: a particle effect its keys tint and scale.
/// </summary>
public sealed class ParticleGlowHelper : ParticleHelper
{
    private const int TintControlPoint = 16;
    private const int ScaleControlPoint = 17;

    /// <summary>Initializes the glow of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public ParticleGlowHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    protected override void SetControlPoints(ParticleSceneNode effect)
    {
        base.SetControlPoints(effect);

        var keyValues = Owner.KeyValues;

        effect.GetControlPoint(TintControlPoint).Position = keyValues.GetVector3Property("colortint", new Vector3(255f));
        effect.GetControlPoint(ScaleControlPoint).Position = new Vector3(
            keyValues.GetFloatProperty("alphascale", 1f),
            keyValues.GetFloatProperty("scale", 1f),
            keyValues.GetFloatProperty("selfillumscale", 1f));

        if (keyValues.GetStringProperty("effect_textureOverride") is { Length: > 0 } textureOverride)
        {
            effect.SetTextureOverride(textureOverride);
        }
    }
}
