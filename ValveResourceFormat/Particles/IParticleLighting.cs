namespace ValveResourceFormat.Particles;

/// <summary>
/// The scene lighting a particle system can query.
/// </summary>
public interface IParticleLighting
{
    /// <summary>Lighting for systems nothing lights: white everywhere, as no world lighting answers.</summary>
    static IParticleLighting Unlit { get; } = new UnlitLighting();

    /// <summary>The indirect (ambient) light arriving at a world position.</summary>
    /// <param name="position">World position to sample at.</param>
    /// <returns>Linear light colour.</returns>
    Vector3 SampleAmbientLight(Vector3 position) => Vector3.One;

    // todo: sample from ambient cube octree?

    private sealed class UnlitLighting : IParticleLighting;
}
