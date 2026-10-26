using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The glow of an <c>env_particle_glow</c>: a particle effect its keys tint and scale.
/// </summary>
internal sealed class ParticleGlowHelper : ParticleHelper
{
    private ParticleGlowHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
    }

    /// <summary>Creates the glow a class declares.</summary>
    /// <returns>The glow, or <see langword="null"/> when the entity names no effect that exists, so that the entity is drawn some other way.</returns>
    internal static new ParticleGlowHelper? Create(IHelperOwner owner, HelperDeclaration declaration)
        => HasEffect(owner, declaration) ? new ParticleGlowHelper(owner, declaration) : null;

    /// <inheritdoc/>
    protected override void SetControlPoints(ParticleSceneNode effect)
    {
        base.SetControlPoints(effect);
        EnvParticleGlow.SetGlowControlPoints(effect, Owner.KeyValues);
    }
}
