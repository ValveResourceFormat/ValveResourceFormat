using ValveResourceFormat.Particles;

namespace ValveResourceFormat.Renderer.Particles;

/// <summary>
/// Particle collision against a scene: the <see cref="Rubikon.DefaultGeometry"/> of its
/// <see cref="Scene.PhysicsWorld"/>, read at trace time, so geometry loaded after an effect is still
/// collided with.
/// </summary>
internal sealed class ParticleSceneCollision(Scene scene) : IParticleCollision
{
    /// <inheritdoc/>
    public bool HasGeometry => scene.PhysicsWorld is { IsEmpty: false };

    /// <inheritdoc/>
    public bool TraceRay(Vector3 rayStart, Vector3 rayEnd, out Vector3 position, out Vector3 normal, out float fraction)
    {
        if (scene.PhysicsWorld is { } physics)
        {
            return physics.TraceRay(rayStart, rayEnd, Rubikon.DefaultGeometry, out position, out normal, out fraction);
        }

        return IParticleCollision.None.TraceRay(rayStart, rayEnd, out position, out normal, out fraction);
    }
}
