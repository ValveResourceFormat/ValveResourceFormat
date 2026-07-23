using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>A playing (or pending) instance of a sound event definition.</summary>
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
    /// e.g. footsteps play 20 units above the ground).
    /// </summary>
    public Vector3 PositionOffset { get; protected set; }

    /// <summary>Gets or sets a volume passed by game code, replacing the definition's volume property.</summary>
    public float? VolumeOverride { get; set; }

    /// <summary>Gets the sound event definition this instance was built from.</summary>
    public SoundEventDefinition Definition { get; }

    /// <summary>Gets the vsnd file currently playing for this event.</summary>
    public string? PlayingSoundFile { get; protected set; }

    /// <summary>Collects the position and vsnd name of every audible positioned sound in this event tree.</summary>
    public void CollectDebugSounds(List<(Vector3 Position, string Text)> results)
    {
        if (Playing && Position.HasValue && PlayingSoundFile != null)
        {
            results.Add((Position.Value + PositionOffset, PlayingSoundFile));
        }

        foreach (var child in ChildSoundEvents)
        {
            child.CollectDebugSounds(results);
        }
    }

    /// <summary>Gets the key-values the definition was parsed from.</summary>
    public KVObject SoundEventData => Definition.Data;

    /// <summary>Gets the combined sample provider for this event.</summary>
    public SampleProviderMulti SampleProvider { get; private set; } = null!;
    /// <summary>Gets the child sound events spawned by this event.</summary>
    protected List<SoundEvent> ChildSoundEvents { get; } = [];
    /// <summary>Gets the sample providers built by <see cref="DoStart"/>.</summary>
    protected List<AudioSampleProvider> SampleProviders { get; } = [];

    // Backing state for BuildTrackProvider/StartChildren: reused across retriggers instead of
    // rebuilding the provider/child tree from scratch every time DoStart() runs.
    private CachedSoundSampleProvider? trackSource;
    private SampleProvider2D? unspatializedTrackSource;
    private SampleProvider3D? spatializedTrackSource;
    private SoundEvent?[]? children;

    /// <summary>Gets the random source for randomized event properties (track picking, volume/pitch jitter, retrigger intervals).</summary>
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

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(SampleProvider))]
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

        if (SampleProviders.Count == 0 && !WaitingToStart)
        {
            // Nothing in this event can ever produce samples (e.g. a definition with no tracks and no
            // children): its provider never reaches the mixer, so no end-of-sound can fire - stop now
            // instead of sitting in the mixer's active set forever.
            Stop();
            return;
        }

        if (!Started)
        {
            Started = true;
            OnStart?.Invoke(this);
        }
    }

    /// <summary>Gets whether the event is intentionally silent right now but scheduled to produce sound later (e.g. waiting out its first retrigger interval).</summary>
    private protected virtual bool WaitingToStart => false;

    /// <summary>
    /// Gets the curve <see cref="FadeOutAndStop"/> fades along, or null to always use its linear fallback.
    /// Types that author a stop-fade curve (e.g. CS:GO's "fadetime_volume_mapping_curve") override this.
    /// </summary>
    private protected virtual SoundEventCurve? FadeOutCurve => null;

    /// <summary>
    /// Gets whether the event is fading out towards a stop (see <see cref="FadeOutAndStop"/>).
    /// Retriggers are suppressed while fading.
    /// </summary>
    public bool FadingOut { get; private set; }

    /// <summary>Stopwatch timestamp of the next occlusion retrace (see <see cref="Update"/>).</summary>
    private long nextOcclusionTraceTimestamp;

    /// <summary>
    /// Fades the whole event tree out along <see cref="FadeOutCurve"/> (or linearly over
    /// <paramref name="fallbackSeconds"/> when the event has none) and stops it when the fade completes.
    /// </summary>
    public void FadeOutAndStop(float fallbackSeconds = 1f)
    {
        if (!Started || FadingOut)
        {
            return;
        }

        FadingOut = true;
        SampleProvider.BeginFadeOut(FadeOutCurve, fallbackSeconds, SampleRate);
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

    /// <summary>
    /// Starts another sound event as a child of this one, mixed into this event's output. Callers may pass
    /// the same instance again on a later retrigger (e.g. cached by child index) instead of building a new
    /// one - already-wired instances are recognized and only restarted, not rebuilt.
    /// </summary>
    protected void StartAsChild(SoundEvent childSoundEvent)
    {
        // "set_child_position": the child follows this event (a footstep's gear rustle plays at the player).
        // Otherwise the child uses its own authored position (a soundscape's birds sit in their own tree).
        if (Definition.SetChildPosition)
        {
            childSoundEvent.Position = Position;
        }

        if (childSoundEvent.SampleProvider is null)
        {
            childSoundEvent.Init(Mixer, SampleRate);
            childSoundEvent.OnSoundStart += ChildSoundStarted;
            childSoundEvent.OnSoundOver += ChildSoundOver;
        }

        ChildSoundEvents.Add(childSoundEvent);
        SampleProviders.Add(childSoundEvent.SampleProvider);
        childSoundEvent.Start();
    }

    /// <summary>
    /// Builds (or reuses, on a later retrigger) a leaf provider streaming <paramref name="cachedSound"/>,
    /// wrapped for 3D playback at <paramref name="position"/> when given, otherwise unspatialized. The
    /// returned provider's <see cref="AudioSampleProvider.Volume"/> (and, for a <see cref="SampleProvider3D"/>,
    /// its Range/DistanceVolumeCurve/StereoMixCurve) are left at their defaults - callers set those afterwards.
    /// For definitions that play one track at a time; call once per <see cref="DoStart"/>.
    /// </summary>
    protected AudioSampleProvider BuildTrackProvider(CachedSound cachedSound, Vector3? position, float pitch, int delaySamples)
    {
        trackSource ??= new CachedSoundSampleProvider(cachedSound);
        trackSource.Reset(cachedSound);
        trackSource.Pitch = pitch;
        trackSource.DelaySamples = delaySamples;

        if (position.HasValue)
        {
            var spatial = spatializedTrackSource ??= new SampleProvider3D(trackSource);
            spatial.Position = position.Value;
            spatial.ResetInterpolation();
            return spatial;
        }

        return unspatializedTrackSource ??= new SampleProvider2D(trackSource);
    }

    /// <summary>
    /// Starts (or restarts, on a later retrigger) one child per entry in <paramref name="definitions"/>: builds
    /// each the first time and reuses the same instance afterwards instead of rebuilding its whole provider
    /// subtree from scratch every time. Null entries (unresolved definitions) are skipped.
    /// Call once per <see cref="DoStart"/> when a definition plays a fixed set of child events.
    /// </summary>
    protected void StartChildren(SoundEventDefinition?[] definitions)
    {
        children ??= new SoundEvent?[definitions.Length];

        for (var i = 0; i < definitions.Length; i++)
        {
            var definition = definitions[i];
            if (definition == null)
            {
                continue;
            }

            var child = children[i] ??= Build(definition);
            if (child != null)
            {
                StartAsChild(child);
            }
        }
    }

    private void ChildSoundOver(SoundEvent soundEvent)
    {
        // Nothing to do: one child ending must not silence the container while siblings still play.
        // The all-quiet case is handled by our own SampleProvider firing OnOver, which calls OnFinished.
    }

    private void ChildSoundStarted(SoundEvent soundEvent)
    {
        // The child's provider was auto-removed from our mix when it ran dry (e.g. between retriggers);
        // put it back now that it produces samples again (AddProvider is idempotent)
        SampleProvider.AddProvider(soundEvent.SampleProvider);

        if (!Playing)
        {
            OnStarted();
        }
    }

    /// <summary>
    /// Gets whether any child event is still active (e.g. waiting on its own retrigger).
    /// </summary>
    protected bool AnyChildStarted()
    {
        foreach (var child in ChildSoundEvents)
        {
            if (child.Started)
            {
                return true;
            }
        }

        return false;
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

        if (FadingOut)
        {
            // The fade ran to completion, finish the stop
            Stop();
        }
    }

    /// <summary>Marks the event as audible and raises <see cref="OnSoundStart"/>.</summary>
    protected virtual void OnStarted()
    {
        Playing = true;
        OnSoundStart?.Invoke(this);
    }

    /// <summary>Updates spatialization and time-based behavior. Returns whether any sample provider is currently audible.</summary>
    public virtual bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        var anyPlaying = false;

        var occlusionTrace = Definition.OcclusionIntensity > 0f ? Mixer.Player.OcclusionTrace : null;

        if (occlusionTrace != null)
        {
            // Occlusion is smoothed, so it does not need a ray every frame: retrace ~10 times
            // a second, with a jittered interval so concurrent events spread across frames
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now < nextOcclusionTraceTimestamp)
            {
                occlusionTrace = null;
            }
            else
            {
                var interval = 0.08f + 0.04f * Random.NextSingle();
                nextOcclusionTraceTimestamp = now + (long)(interval * System.Diagnostics.Stopwatch.Frequency);
            }
        }

        foreach (var provider in SampleProviders)
        {
            if (provider is SampleProvider3D spatialProvider)
            {
                if (Position.HasValue)
                {
                    spatialProvider.Position = Position.Value + PositionOffset;
                }

                if (occlusionTrace != null)
                {
                    spatialProvider.OcclusionTarget = occlusionTrace(listenerPosition, spatialProvider.Position)
                        ? 1f - Definition.OcclusionIntensity
                        : 1f;
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
    /// Detaches all event subscribers. The sound event is a fire-and-forget handle whose lifetime
    /// the mixer owns, so it is not <see cref="IDisposable"/>.
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
            "citadel_default_2d" or "citadel_ambient_3d" => new SoundEventCitadelAmbient(definition),
            "hlvr_default_3d" or "hlvr_2d_w_occlusion" or "src1_3d" or "src1_2d" => new SoundEventHLVRDefault(definition),
            "hlvr_start_multi" => new SoundEventHLVRMulti(definition),
            _ => null,
        };
    }

    /// <summary>
    /// Reads a property that is either an array of strings or a single string (a common shorthand in
    /// Source 2 script data for "one or more of these", e.g. a track list with only one entry).
    /// </summary>
    internal static string[] GetStringOrArrayProperty(KVObject data, string name)
    {
        if (!data.TryGetValue(name, out var value))
        {
            return [];
        }

        if (value.ValueType == KVValueType.Array)
        {
            return data.GetArray<string>(name) ?? [];
        }

        var single = data.GetStringProperty(name);
        return single != null ? [single] : [];
    }
}
