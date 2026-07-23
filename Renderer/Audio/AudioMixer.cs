using ValveResourceFormat.Renderer.Audio.SampleProviders;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Tracks active sound events and mixes their sample providers into a single continuous stereo stream.
/// </summary>
public sealed class AudioMixer : IDisposable
{
    internal SoundEventPlayer Player { get; }

    private readonly SampleProviderMixer root = new();
    private readonly HashSet<SoundEvent> soundEvents = [];

    /// <summary>
    /// Collects the position and vsnd name of every audible positioned sound into <paramref name="positioned"/>,
    /// and the vsnd name of every audible non-positioned (2D) sound into <paramref name="flat"/>.
    /// </summary>
    public void CollectDebugSounds(List<(Vector3 Position, string Text)> positioned, List<string> flat)
    {
        lock (soundEvents)
        {
            foreach (var soundEvent in soundEvents)
            {
                soundEvent.CollectDebugSounds(positioned, flat);
            }
        }
    }

    // Update runs every frame, so it copies into a reused list instead of allocating a snapshot array.
    // The copy is still needed: updating an event can start a retrigger, which mutates the set.
    private readonly List<SoundEvent> updateSnapshot = [];

    internal AudioMixer(SoundEventPlayer player)
    {
        Player = player;
    }

    private Vector3 listenerPosition;
    private Vector3 listenerRightEarDirection = Vector3.UnitY;

    /// <summary>Gets the listener position from the most recent <see cref="Update"/>.</summary>
    internal Vector3 ListenerPosition => listenerPosition;

    /// <summary>
    /// Applies the current listener state to a sound event immediately.
    /// </summary>
    internal void PrimeListener(SoundEvent soundEvent)
    {
        soundEvent.Update(listenerPosition, listenerRightEarDirection);
    }

    /// <summary>
    /// Updates spatialization for all active sound events.
    /// </summary>
    public void Update(Vector3 listenerPosition, Vector3 listenerForward)
    {
        var rightEarDirection = Vector3.Cross(Vector3.UnitZ, listenerForward);
        if (rightEarDirection.LengthSquared() > float.Epsilon)
        {
            rightEarDirection = Vector3.Normalize(rightEarDirection);
        }

        this.listenerPosition = listenerPosition;
        listenerRightEarDirection = rightEarDirection;

        lock (soundEvents)
        {
            updateSnapshot.Clear();
            updateSnapshot.AddRange(soundEvents);
        }

        for (var i = 0; i < updateSnapshot.Count; i++)
        {
            updateSnapshot[i].Update(listenerPosition, rightEarDirection);
        }
    }

    /// <summary>
    /// Mixes all active sounds into the buffer. Always fills the full buffer.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        return root.Read(buffer, offset, count);
    }

    internal void Register(SoundEvent soundEvent)
    {
        soundEvent.OnSoundStart += SoundEvent_OnSoundStart;
        soundEvent.OnSoundOver += SoundEvent_OnSoundOver;
        soundEvent.OnStart += SoundEvent_OnStart;
        soundEvent.OnStop += SoundEvent_OnStop;
    }

    private void SoundEvent_OnStart(SoundEvent soundEvent)
    {
        lock (soundEvents)
        {
            soundEvents.Add(soundEvent);
        }
    }

    private void SoundEvent_OnStop(SoundEvent soundEvent)
    {
        lock (soundEvents)
        {
            soundEvents.Remove(soundEvent);
        }

        root.RemoveProvider(soundEvent.SampleProvider);
    }

    private void SoundEvent_OnSoundStart(SoundEvent soundEvent)
    {
        root.AddProvider(soundEvent.SampleProvider);
    }

    private void SoundEvent_OnSoundOver(SoundEvent soundEvent)
    {
        root.RemoveProvider(soundEvent.SampleProvider);
    }

    /// <summary>Stops and disposes all active sound events.</summary>
    public void Dispose()
    {
        SoundEvent[] snapshot;
        lock (soundEvents)
        {
            snapshot = [.. soundEvents];
            soundEvents.Clear();
        }

        foreach (var soundEvent in snapshot)
        {
            soundEvent.Cleanup();
        }

        root.ClearProviders();
    }
}
