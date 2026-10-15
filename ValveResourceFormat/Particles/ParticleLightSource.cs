namespace ValveResourceFormat.Particles;

/// <summary>
/// A point light a particle system places in the scene on its own, rather than through a renderer.
/// The function that owns it keeps the same instance for the life of the system, so it identifies
/// the light across <see cref="IParticleSystemObserver.OnLightSourceUpdated"/> calls.
/// </summary>
public sealed class ParticleLightSource
{
    /// <summary>Whether the light is in the scene. The other values only describe it while it is.</summary>
    public bool IsLit { get; internal set; }

    /// <summary>The world position of the light.</summary>
    public Vector3 Position { get; internal set; }

    /// <summary>
    /// The linear colour scaled by the intensity. The light falls off with the inverse square of the
    /// distance, reaching this value one unit away.
    /// </summary>
    public Vector3 Color { get; internal set; }

    /// <summary>How far the light reaches.</summary>
    public float Range { get; internal set; }
}
