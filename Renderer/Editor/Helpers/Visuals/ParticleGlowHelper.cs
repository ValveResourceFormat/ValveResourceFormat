using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The glow of an <c>env_particle_glow</c>: a particle effect its keys tint and scale.
/// </summary>
internal sealed class ParticleGlowHelper(IHelperOwner owner, FgdHelper declaration) : ParticleHelper(owner, declaration)
{
    private const int TintControlPoint = 16;
    private const int ScaleControlPoint = 17;

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
