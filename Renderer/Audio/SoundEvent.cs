using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A playing (or pending) instance of a sound event definition.
/// Returned by <see cref="SoundEventPlayer.Play"/> as a handle to control and reposition the sound.
/// </summary>
public abstract class SoundEvent : IDisposable
{
    /// <summary>Raised when the event begins producing audible samples.</summary>
    public event Action<SoundEvent>? OnSoundStart;

    /// <summary>Raised when the event has run out of samples.</summary>
    public event Action<SoundEvent>? OnSoundOver;

    /// <summary>Raised when the event becomes active in the mixer.</summary>
    public event Action<SoundEvent>? OnStart;

    /// <summary>Raised when the event is removed from the mixer.</summary>
    public event Action<SoundEvent>? OnStop;

    public bool Playing { get; protected set; }
    public bool Started { get; private set; }

    /// <summary>
    /// Gets or sets the world position of the sound. Null plays the sound without spatialization (e.g. UI or first person sounds).
    /// Can be updated while the sound is playing to move it.
    /// </summary>
    public Vector3? Position { get; set; }

    public KVObject SoundEventData { get; }

    public SampleProviderMulti SampleProvider { get; private set; } = null!;
    protected List<SoundEvent> ChildSoundEvents { get; } = [];
    protected List<AudioSampleProvider> SampleProviders { get; } = [];

    protected AudioMixer Mixer { get; private set; } = null!;
    protected int SampleRate { get; private set; }

    protected SoundEvent(KVObject soundEventData)
    {
        SoundEventData = soundEventData;
    }

    internal void Init(AudioMixer mixer, int sampleRate)
    {
        Mixer = mixer;
        SampleRate = sampleRate;

        SampleProvider = new SampleProviderMulti(SampleProviders);
        SampleProvider.OnOver += OnFinished;
    }

    /// <summary>
    /// Starts (or restarts, in the case of retriggered events) the sound event.
    /// </summary>
    public void Start()
    {
        SampleProviders.Clear();
        ChildSoundEvents.Clear();
        SampleProvider.ClearProviders();

        DoStart();

        if (SampleProviders.Count > 0)
        {
            foreach (var provider in SampleProviders)
            {
                SampleProvider.AddProvider(provider);
            }

            if (!Playing)
            {
                OnStarted();
            }
        }
        else if (Playing)
        {
            OnFinished();
        }

        if (!Started)
        {
            Started = true;
            OnStart?.Invoke(this);
        }
    }

    /// <summary>
    /// Stops the sound event and any child events it has spawned.
    /// </summary>
    public void Stop()
    {
        if (Playing)
        {
            OnFinished();
        }

        if (Started)
        {
            Started = false;
            OnStop?.Invoke(this);
        }

        foreach (var child in ChildSoundEvents)
        {
            child.Stop();
        }
    }

    protected void StartAsChild(SoundEvent childSoundEvent)
    {
        childSoundEvent.Position = Position;
        childSoundEvent.Init(Mixer, SampleRate);
        ChildSoundEvents.Add(childSoundEvent);
        SampleProviders.Add(childSoundEvent.SampleProvider);
        childSoundEvent.OnSoundStart += ChildSoundStarted;
        childSoundEvent.OnSoundOver += ChildSoundOver;
        childSoundEvent.Start();
    }

    private void ChildSoundOver(SoundEvent soundEvent)
    {
        if (Playing)
        {
            OnFinished();
        }
    }

    private void ChildSoundStarted(SoundEvent soundEvent)
    {
        if (!Playing)
        {
            OnStarted();
        }
    }

    /// <summary>
    /// Builds the sample providers (and child events) for this event based on its definition.
    /// </summary>
    protected abstract void DoStart();

    protected virtual void OnFinished()
    {
        Playing = false;
        OnSoundOver?.Invoke(this);
    }

    protected virtual void OnStarted()
    {
        Playing = true;
        OnSoundStart?.Invoke(this);
    }

    /// <summary>
    /// Called every frame while the event is active to update spatialization and time based behavior.
    /// Returns whether any sample provider is currently audible.
    /// </summary>
    public virtual bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        var anyPlaying = false;

        foreach (var provider in SampleProviders)
        {
            if (provider is SampleProvider3D spatialProvider)
            {
                if (Position.HasValue)
                {
                    spatialProvider.Position = Position.Value;
                }

                if (spatialProvider.Update(listenerPosition, rightEarDirection))
                {
                    anyPlaying = true;
                }
            }
        }

        foreach (var child in ChildSoundEvents)
        {
            if (child.Update(listenerPosition, rightEarDirection))
            {
                anyPlaying = true;
            }
        }

        return anyPlaying;
    }

    public virtual void Dispose()
    {
        OnSoundOver = null;
        OnSoundStart = null;
        OnStart = null;
        OnStop = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates a sound event instance for the given definition, or null when the event type is not supported.
    /// </summary>
    public static SoundEvent? Build(KVObject soundEventData)
    {
        var type = soundEventData.GetStringProperty("type", string.Empty);

        return type switch
        {
            "csgo_mega" => new SoundEventCSGOMega(soundEventData),
            _ => null,
        };
    }
}
