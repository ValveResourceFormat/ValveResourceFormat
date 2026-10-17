using ValveResourceFormat.Renderer.Editor.Gizmos;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The glow of an <c>env_particle_glow</c>: a particle effect its keys tint and scale.
/// </summary>
internal sealed class ParticleGlowHelper(IHelperOwner owner, HelperDeclaration declaration) : ParticleHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override GizmoTransformModes SupportedTransformModes => GizmoTransformModes.Translate | GizmoTransformModes.Rotate;

    /// <inheritdoc/>
    protected override void SetControlPoints(ParticleSceneNode effect)
    {
        base.SetControlPoints(effect);
        EnvParticleGlow.SetGlowControlPoints(effect, Owner.KeyValues);
    }
}
