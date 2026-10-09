using ValveResourceFormat.Particles;

namespace ValveResourceFormat.Renderer.Particles;

/// <summary>
/// Particle collision against a scene: the <see cref="Rubikon.DefaultGeometry"/> shapes of its
/// <see cref="Scene.PhysicsWorld"/>, and its <see cref="Scene.CollisionGroundPlane"/>. Both are read at
/// trace time, so geometry loaded after an effect is still collided with.
/// </summary>
internal sealed class ParticleSceneCollision(Scene scene) : IParticleCollision
{
    /// <inheritdoc/>
    public bool HasGeometry => scene.PhysicsWorld is { IsEmpty: false } || scene.CollisionGroundPlane.HasValue;

    /// <inheritdoc/>
    public bool TraceRay(Vector3 rayStart, Vector3 rayEnd, out ParticleTraceHit hit)
    {
        hit = default;

        var length = Vector3.Distance(rayStart, rayEnd);

        if (length <= float.Epsilon)
        {
            return false;
        }

        var nearest = float.MaxValue;

        if (scene.PhysicsWorld is { IsEmpty: false } physics)
        {
            var result = physics.TraceRay(rayStart, rayEnd, Rubikon.DefaultGeometry);

            if (result.Hit)
            {
                nearest = result.Distance;
                hit = new ParticleTraceHit(result.HitPosition, result.HitNormal, result.Distance / length);
            }
        }

        if (scene.CollisionGroundPlane is { } height && rayStart.Z >= height && rayEnd.Z < height)
        {
            var fraction = (rayStart.Z - height) / (rayStart.Z - rayEnd.Z);

            if (fraction * length < nearest)
            {
                nearest = fraction * length;
                hit = new ParticleTraceHit(Vector3.Lerp(rayStart, rayEnd, fraction), Vector3.UnitZ, fraction);
            }
        }

        return nearest != float.MaxValue;
    }
}
