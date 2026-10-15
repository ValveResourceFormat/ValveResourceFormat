namespace ValveResourceFormat.Particles;

/// <summary>
/// Watches a simulated particle system so a drawing layer can keep up with it. The simulation holds
/// one per system and draws nothing itself, which is what lets it run without a graphics backend.
/// </summary>
public interface IParticleSystemObserver
{
    /// <summary>
    /// Called after every simulated step of the system, and never while it fast-forwards through a
    /// pre-simulation; once that is done it is called once.
    /// </summary>
    /// <param name="particles">The system's live particles.</param>
    /// <param name="state">The system's render state.</param>
    void OnFrameSimulated(ParticleCollection particles, ParticleSystemState state);

    /// <summary>
    /// Called when particles of the system raise an event, before particles being killed are removed,
    /// so <paramref name="indices"/> still address them in <paramref name="particles"/>.
    /// </summary>
    /// <param name="particles">The system's live particles.</param>
    /// <param name="eventType">The event, a single flag.</param>
    /// <param name="indices">The indices of the particles that raised it, ascending.</param>
    void OnParticleEvent(ParticleCollection particles, ParticleEventType eventType, ReadOnlySpan<int> indices)
    {
    }
}
