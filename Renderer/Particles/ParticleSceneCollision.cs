using ValveResourceFormat.Particles;

namespace ValveResourceFormat.Renderer.Particles;

/// <summary>
/// Particle collision against a scene: the physics of the world it belongs to, plus the optional
/// ground plane a preview stands its effect on. Both are read at trace time, so geometry loaded after
/// the effect is still collided with.
/// </summary>
internal sealed class ParticleSceneCollision(Scene scene) : IParticleCollision
{
    /// <inheritdoc/>
    public bool HasGeometry => scene.PhysicsWorld is { IsEmpty: false } || scene.CollisionGroundPlane.HasValue;

    /// <inheritdoc/>
    public bool TraceRay(Vector3 from, Vector3 to, out ParticleTraceHit hit)
    {
        hit = default;

        var length = Vector3.Distance(from, to);

        if (length <= float.Epsilon)
        {
            return false;
        }

        var nearest = float.MaxValue;

        if (scene.PhysicsWorld is { IsEmpty: false } physics)
        {
            var result = physics.TraceRay(from, to, Rubikon.DefaultGeometry);

            if (result.Hit)
            {
                nearest = result.Distance;
                hit = new ParticleTraceHit(result.HitPosition, result.HitNormal, result.Distance / length);
            }
        }

        // The ground plane is one-sided: it stops what comes down onto it from above
        if (scene.CollisionGroundPlane is { } height && from.Z >= height && to.Z < height)
        {
            var fraction = (from.Z - height) / (from.Z - to.Z);

            if (fraction * length < nearest)
            {
                nearest = fraction * length;
                hit = new ParticleTraceHit(Vector3.Lerp(from, to, fraction), Vector3.UnitZ, fraction);
            }
        }

        return nearest != float.MaxValue;
    }
}
