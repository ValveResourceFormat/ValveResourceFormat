using System.Diagnostics;
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

    private ListenerState listener = new(Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ, Vector3.Zero, 0f);
    private long lastUpdateTimestamp;

    // Longer than this and the listener did not walk, it was placed there (a load, a camera jump, a
    // paused viewer coming back): the implied velocity is meaningless, so the frame runs without one.
    private const float MaxListenerSpeed = 5000f;

    // Beyond this the gap is a stall rather than a frame, and smoothing a whole second of it in one
    // step just snaps whatever it drives.
    private const float MaxDeltaTime = 0.25f;

    /// <summary>Gets the listener position from the most recent <see cref="Update"/>.</summary>
    internal Vector3 ListenerPosition => listener.Position;

    /// <summary>
    /// Applies the current listener state to a sound event immediately. No time has passed since the last
    /// update, so this primes spatialization without advancing anything time-based.
    /// </summary>
    internal void PrimeListener(SoundEvent soundEvent)
    {
        soundEvent.Update(listener with { DeltaTime = 0f });
    }

    /// <summary>
    /// Updates spatialization for all active sound events. The three direction vectors are the listener's
    /// own head frame - taken from the camera rather than rebuilt here, since deriving them by cross
    /// product has no answer when the listener looks straight up or straight down.
    /// </summary>
    public void Update(Vector3 listenerPosition, Vector3 listenerForward, Vector3 listenerRight, Vector3 listenerUp)
    {
        var now = Stopwatch.GetTimestamp();
        var deltaTime = lastUpdateTimestamp == 0
            ? 0f
            : Math.Clamp((float)Stopwatch.GetElapsedTime(lastUpdateTimestamp).TotalSeconds, 0f, MaxDeltaTime);
        lastUpdateTimestamp = now;

        var velocity = Vector3.Zero;

        if (deltaTime > 0f)
        {
            velocity = (listenerPosition - listener.Position) / deltaTime;

            if (velocity.LengthSquared() > MaxListenerSpeed * MaxListenerSpeed)
            {
                velocity = Vector3.Zero;
            }
        }

        listener = new ListenerState(listenerPosition, listenerForward, listenerRight, listenerUp, velocity, deltaTime,
            Player.PositionalSharpness, Player.SpectralCueStrength, Player.SampleRate);

        lock (soundEvents)
        {
            updateSnapshot.Clear();
            updateSnapshot.AddRange(soundEvents);
        }

        for (var i = 0; i < updateSnapshot.Count; i++)
        {
            updateSnapshot[i].Update(listener);
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
