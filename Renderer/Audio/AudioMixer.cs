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

    internal AudioMixer(SoundEventPlayer player)
    {
        Player = player;
    }

    /// <summary>
    /// Updates spatialization for all active sound events. Call once per frame from the render/game thread.
    /// </summary>
    public void Update(Vector3 listenerPosition, Vector3 listenerForward)
    {
        var rightEarDirection = Vector3.Cross(Vector3.UnitZ, listenerForward);
        if (rightEarDirection.LengthSquared() > float.Epsilon)
        {
            rightEarDirection = Vector3.Normalize(rightEarDirection);
        }

        SoundEvent[] snapshot;
        lock (soundEvents)
        {
            snapshot = [.. soundEvents];
        }

        foreach (var soundEvent in snapshot)
        {
            soundEvent.Update(listenerPosition, rightEarDirection);
        }
    }

    /// <summary>
    /// Mixes all active sounds into the buffer. Called from the mixing thread. Always fills the full buffer.
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
            soundEvent.Dispose();
        }

        root.ClearProviders();
    }
}
