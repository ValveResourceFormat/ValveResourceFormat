using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A playing (or pending) instance of a sound event definition.
/// Returned by <see cref="SoundEventPlayer.Play"/> as a handle to control and reposition the sound.
/// </summary>
public abstract class SoundEvent
{
    /// <summary>Raised when the event begins producing audible samples.</summary>
    public event Action<SoundEvent>? OnSoundStart;

    /// <summary>Raised when the event has run out of samples.</summary>
    public event Action<SoundEvent>? OnSoundOver;

    /// <summary>Raised when the event becomes active in the mixer.</summary>
    public event Action<SoundEvent>? OnStart;

    /// <summary>Raised when the event is removed from the mixer.</summary>
    public event Action<SoundEvent>? OnStop;

    /// <summary>Gets whether the event is currently producing audible samples.</summary>
    public bool Playing { get; protected set; }
    /// <summary>Gets whether the event is active in the mixer (it may be momentarily silent, e.g. between retriggers).</summary>
    public bool Started { get; private set; }

    /// <summary>
    /// Gets or sets the world position of the sound. Null plays the sound without spatialization (e.g. UI or first person sounds).
    /// Can be updated while the sound is playing to move it.
    /// </summary>
    public Vector3? Position { get; set; }

    /// <summary>
    /// Gets or sets the offset added to <see cref="Position"/> ("position_offset" in the event data,
    /// e.g. footsteps play 20 units above the ground). Applied on top of Position so repositioning a
    /// playing sound keeps the offset.
    /// </summary>
    public Vector3 PositionOffset { get; protected set; }

    /// <summary>
    /// Gets or sets a volume passed by game code, replacing the definition's volume property.
    /// </summary>
    public float? VolumeOverride { get; set; }

    /// <summary>Gets the sound event definition this instance was built from.</summary>
    public SoundEventDefinition Definition { get; }

    /// <summary>Gets the key-values the definition was parsed from.</summary>
    public KVObject SoundEventData => Definition.Data;

    /// <summary>Gets the combined sample provider for this event, fed to the mixer.</summary>
    public SampleProviderMulti SampleProvider { get; private set; } = null!;
    /// <summary>Gets the child sound events spawned by this event.</summary>
    protected List<SoundEvent> ChildSoundEvents { get; } = [];
    /// <summary>Gets the sample providers built by <see cref="DoStart"/>.</summary>
    protected List<AudioSampleProvider> SampleProviders { get; } = [];

    /// <summary>
    /// Gets the random source for randomized event properties (track picking, volume/pitch jitter, retrigger intervals).
    /// Shared by the player and reseeded with the play time on every <see cref="SoundEventPlayer.Play"/>.
    /// </summary>
    private protected SoundRandom Random => Mixer.Player.Random;

    /// <summary>Gets the mixer this event plays through.</summary>
    protected AudioMixer Mixer { get; private set; } = null!;
    /// <summary>Gets the mixer output sample rate.</summary>
    protected int SampleRate { get; private set; }

    /// <summary>Creates a sound event instance for the given definition.</summary>
    protected SoundEvent(SoundEventDefinition definition)
    {
        Definition = definition;
    }

    internal void Init(AudioMixer mixer, int sampleRate)
    {
        Mixer = mixer;
        SampleRate = sampleRate;

        // Start() fills the providers in, no point seeding them here
        SampleProvider = new SampleProviderMulti();
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
            // Prime spatialization before the mixer can read the providers,
            // so the sound does not start with zeroed volumes and lose its attack transient
            Mixer.PrimeListener(this);

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

    /// <summary>Starts another sound event as a child of this one, mixed into this event's output.</summary>
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

    /// <summary>Marks the event as no longer audible and raises <see cref="OnSoundOver"/>.</summary>
    protected virtual void OnFinished()
    {
        Playing = false;
        OnSoundOver?.Invoke(this);
    }

    /// <summary>Marks the event as audible and raises <see cref="OnSoundStart"/>.</summary>
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
                    spatialProvider.Position = Position.Value + PositionOffset;
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

    /// <summary>
    /// Detaches all event subscribers. Called by the mixer when the player is torn down; the sound event is a
    /// fire-and-forget handle whose lifetime the mixer owns, so it is not <see cref="IDisposable"/>.
    /// </summary>
    internal void Cleanup()
    {
        OnSoundOver = null;
        OnSoundStart = null;
        OnStart = null;
        OnStop = null;
    }

    /// <summary>
    /// Creates a sound event instance for the given definition, or null when the event type is not supported.
    /// </summary>
    public static SoundEvent? Build(SoundEventDefinition definition)
    {
        return definition.Type switch
        {
            "csgo_mega" => new SoundEventCSGOMega(definition),
            _ => null,
        };
    }
}
