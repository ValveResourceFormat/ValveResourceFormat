namespace ValveResourceFormat.Particles;

/// <summary>
/// The world geometry a particle system can trace against, for collision and ground placement. Traces
/// run between any two points: along a particle's movement, or out from a control point to find the
/// surfaces a collision plane set is built from.
/// </summary>
public interface IParticleCollision
{
    /// <summary>Collision for systems with no world around them: every trace misses.</summary>
    static IParticleCollision None { get; } = new NoCollision();

    /// <summary>Whether there is any geometry at all, so a function that needs it can skip its traces.</summary>
    bool HasGeometry { get; }

    /// <summary>Traces a ray from <paramref name="rayStart"/> to <paramref name="rayEnd"/> against the world.</summary>
    /// <param name="rayStart">Start of the ray.</param>
    /// <param name="rayEnd">End of the ray.</param>
    /// <param name="position">The nearest hit point, or <paramref name="rayEnd"/> on a miss.</param>
    /// <param name="normal">The surface normal at the hit point, facing the ray, or zero on a miss.</param>
    /// <param name="fraction">How far along the ray the hit is, from 0 at its start to 1 at its end, which it is on a miss.</param>
    /// <returns>Whether the ray hit anything.</returns>
    bool TraceRay(Vector3 rayStart, Vector3 rayEnd, out Vector3 position, out Vector3 normal, out float fraction);

    private sealed class NoCollision : IParticleCollision
    {
        public bool HasGeometry => false;

        public bool TraceRay(Vector3 rayStart, Vector3 rayEnd, out Vector3 position, out Vector3 normal, out float fraction)
        {
            position = rayEnd;
            normal = Vector3.Zero;
            fraction = 1f;
            return false;
        }
    }
}
