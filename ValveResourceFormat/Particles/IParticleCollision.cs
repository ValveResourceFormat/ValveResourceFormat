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

    /// <summary>
    /// Finds a ray trace environment by name. These are sets of map volumes traced apart from the
    /// world, such as <c>PRECIPITATION</c> and <c>PRECIPITATIONBLOCKER</c>. A host without them keeps
    /// this default, and the functions that trace them leave particles alone.
    /// </summary>
    /// <param name="name">The environment name.</param>
    /// <returns>The environment's index, or -1 when there is no such environment.</returns>
    int FindRayTraceEnvironment(string name) => -1;

    /// <summary>Traces a ray against a ray trace environment found by <see cref="FindRayTraceEnvironment"/>.</summary>
    /// <param name="environment">The environment index.</param>
    /// <param name="rayStart">Start of the ray.</param>
    /// <param name="direction">Unit direction of the ray.</param>
    /// <param name="maxDistance">How far along the ray to look.</param>
    /// <param name="distance">How far along the ray the nearest hit is.</param>
    /// <param name="normal">The surface normal at the nearest hit, as the volume defines it.</param>
    /// <returns>Whether the ray hit anything within <paramref name="maxDistance"/>.</returns>
    bool TraceRayTraceEnvironment(int environment, Vector3 rayStart, Vector3 direction, float maxDistance, out float distance, out Vector3 normal)
    {
        distance = maxDistance;
        normal = Vector3.Zero;
        return false;
    }

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
