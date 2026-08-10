using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ValveKeyValue;
using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>A playing (or pending) instance of a sound event definition.</summary>
public abstract class SoundEvent
{
    /// <summary>
    /// How deep the sound event graph is walked before giving up. Definitions reference each other by
    /// name, so a child chain, a "base" chain or a "playsoundscape" chain can be cyclic in bad data.
    /// </summary>
    internal const int MaxRecursionDepth = 8;

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

    /// <summary>
    /// Gets or sets an extra multiplier applied on top of whatever volume this event ends up playing at,
    /// including a <see cref="VolumeOverride"/>. Carries a scripted soundscape's "playsoundscape" volume,
    /// which scales everything the soundscape it pulls in plays, and so cascades to child events.
    /// </summary>
    public float VolumeScale { get; set; } = 1f;

    /// <summary>
    /// Gets or sets a playback start delay in seconds, passed by a container, replacing the definition's
    /// "delay" property. Used to stagger otherwise-identical children (e.g. quad-channel ambient beds) so
    /// they do not all loop in phase lock; a negative value seeks ahead into the track instead of adding
    /// silence (see <see cref="SampleProviders.CachedSoundSampleProvider.DelaySamples"/>).
    /// </summary>
    public float? DelayOverride { get; set; }

    /// <summary>Gets the sound event definition this instance was built from.</summary>
    public SoundEventDefinition Definition { get; }

    /// <summary>Gets the vsnd file currently playing for this event.</summary>
    public string? PlayingSoundFile { get; protected set; }

    /// <summary>
    /// Collects the position and vsnd name of every audible positioned sound in this event tree into
    /// <paramref name="positioned"/>, and the vsnd name of every audible non-positioned (2D) sound into
    /// <paramref name="flat"/>.
    /// </summary>
    public void CollectDebugSounds(List<(Vector3 Position, string Text)> positioned, List<string> flat)
    {
        if (Playing && PlayingSoundFile != null)
        {
            if (Position.HasValue)
            {
                positioned.Add((Position.Value + PositionOffset, PlayingSoundFile));
            }
            else
            {
                flat.Add(PlayingSoundFile);
            }
        }

        foreach (var child in ChildSoundEvents)
        {
            child.CollectDebugSounds(positioned, flat);
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

    // BuildTrackProvider/StartChildren reuse these across retriggers rather than rebuild the tree
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

    [MemberNotNull(nameof(SampleProvider))]
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
    /// <returns>
    /// Whether the event became active. False means it was inert - nothing in it could ever produce
    /// samples this play (no tracks and no children, or dropped by its limiter) - and it was stopped
    /// again immediately. Callers must detect that from this return value, not from
    /// <see cref="Started"/>, which a fast mixer may already have cleared again for an ultra-short sound.
    /// </returns>
    public bool Start()
    {
        SampleProviders.Clear();
        ChildSoundEvents.Clear();
        SampleProvider.ClearProviders();

        ApplyDefinitionPlacement();
        DoStart();

        if (SampleProviders.Count > 0)
        {
            // Prime spatialization before the mixer can read the providers, or the sound starts at zero volume and loses its attack transient
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
            // Inert (see the returns doc): the provider never reaches the mixer, so no end-of-sound can fire
            Stop();
            return false;
        }

        if (!Started)
        {
            Started = true;
            OnStart?.Invoke(this);
        }

        return true;
    }

    /// <summary>
    /// Applies the definition's authored placement, right before <see cref="DoStart"/> gets a chance to
    /// override it. A position supplied by the caller (a point_soundevent entity, a parent's
    /// "set_child_position") wins over the definition's own "position" key - all-zero placeholders are
    /// already dropped at parse time - so this only fills in a position nobody else provided.
    /// </summary>
    private void ApplyDefinitionPlacement()
    {
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;
    }

    /// <summary>Gets whether the event is intentionally silent right now but scheduled to produce sound later (e.g. waiting out its first retrigger interval).</summary>
    private protected virtual bool WaitingToStart => waitingForRetrigger;

    private bool wasInitialized;
    private bool waitingForRetrigger;
    private long retriggerTimestamp;

    /// <summary>
    /// Gets the interval, in seconds, this event replays itself on, or null when it does not reschedule.
    /// The default is the shared "enable_retrigger"/"retrigger_interval_*" trio; types that author their
    /// own timer keys (hlvr_ambient_rand, the soundscape script operators) override this.
    /// Only consulted by types that opt into rescheduling from <see cref="StayAliveAfterFinishing"/>.
    /// </summary>
    private protected virtual (float Min, float Max)? RetriggerInterval
        => Definition.EnableRetrigger ? (Definition.RetriggerIntervalMin, Definition.RetriggerIntervalMax) : null;

    /// <summary>Gets whether a replay is already armed and only waiting out its interval.</summary>
    private protected bool RetriggerArmed => waitingForRetrigger;

    /// <summary>
    /// Arms the next replay at a random point in <see cref="RetriggerInterval"/> and returns whether it
    /// did. <see cref="Update"/> performs the replay once the interval elapses.
    /// </summary>
    /// <param name="intervalScale">Fraction of the drawn interval to actually wait out.</param>
    private protected bool CheckRetrigger(float intervalScale = 1f)
    {
        if (RetriggerInterval is not { } interval)
        {
            return false;
        }

        var retriggerAt = float.Lerp(interval.Min, interval.Max, Random.NextSingle()) * intervalScale;
        retriggerTimestamp = Stopwatch.GetTimestamp() + (long)(retriggerAt * Stopwatch.Frequency);
        waitingForRetrigger = true;
        return true;
    }

    /// <summary>
    /// Arms the first replay instead of playing right now, on the first start only: entering a
    /// retriggering event's area should not fire it instantly. Returns whether the calling
    /// <see cref="DoStart"/> should return without starting anything.
    /// </summary>
    /// <param name="intervalScale">Fraction of the drawn interval to wait out before the first play.</param>
    private protected bool WaitOutFirstInterval(float intervalScale = 1f)
    {
        if (wasInitialized)
        {
            return false;
        }

        wasInitialized = true;
        return CheckRetrigger(intervalScale);
    }

    /// <summary>
    /// Gets the curve <see cref="FadeOutAndStop"/> fades along, or null to always use its linear fallback.
    /// Types that author a stop-fade curve (e.g. CS:GO's "fadetime_volume_mapping_curve") override this.
    /// </summary>
    private protected virtual SoundEventCurve? FadeOutCurve => null;

    /// <summary>
    /// Gets the authored stop-fade length in seconds, overriding the fallback <see cref="FadeOutAndStop"/>
    /// is called with. Zero leaves the caller's fallback in place.
    /// </summary>
    private protected virtual float FadeOutSeconds => Definition.FadeOut;

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
        SampleProvider.BeginFadeOut(FadeOutCurve, FadeOutSeconds > 0f ? FadeOutSeconds : fallbackSeconds, SampleRate);
    }

    /// <summary>
    /// Fades the whole event tree in from silence over <paramref name="seconds"/>, so it doesn't jump
    /// straight to full volume the moment it starts. Call right after <see cref="Start"/>; harmless to
    /// call on an event that isn't started yet or already mid-fade-in.
    /// </summary>
    public void FadeIn(float seconds)
    {
        SampleProvider.BeginFadeIn(seconds, SampleRate);
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
        // "set_child_position" pins the child to this event (a footstep's gear rustle plays at the player);
        // otherwise a child keeps its own authored position, and one with neither inherits ours rather than
        // falling back to null and playing unspatialized "in ear".
        if (Definition.SetChildPosition || !childSoundEvent.Definition.Position.HasValue)
        {
            childSoundEvent.Position = Position;
        }

        // A trim applied to this event applies to everything it plays through its children too
        childSoundEvent.VolumeScale = VolumeScale;

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
    /// Picks a track, starts it through <see cref="BuildTrackProvider"/> at this event's position and adds it
    /// to the mix, for the types that play one track at a time. Returns the provider so the caller can apply
    /// its own range and curves, or null when there is nothing to play.
    /// </summary>
    /// <param name="trackNames">The tracks to pick from.</param>
    /// <param name="volume">Volume for the picked track.</param>
    /// <param name="pitch">Pitch multiplier for the picked track.</param>
    /// <param name="range">Audible range, when the track ends up spatialized.</param>
    /// <param name="distanceVolumeCurve">Distance to volume curve, when the track ends up spatialized.</param>
    /// <param name="stereoMixCurve">Distance to unfiltered stereo curve, when the track ends up spatialized.</param>
    /// <param name="delaySeconds">Start delay, defaulting to the definition's own.</param>
    private protected AudioSampleProvider? StartTrack(string[] trackNames, float volume, float pitch, float range,
        SoundEventCurve? distanceVolumeCurve = null, SoundEventCurve? stereoMixCurve = null, float? delaySeconds = null)
    {
        if (trackNames.Length == 0)
        {
            return null;
        }

        var soundName = trackNames[Mixer.Player.PickTrack(Definition, trackNames.Length)];
        var cachedSound = Mixer.Player.SoundCache.GetSound(soundName);
        PlayingSoundFile = soundName;

        if (cachedSound == null)
        {
            return null;
        }

        var position = Position.HasValue ? Position.Value + PositionOffset : (Vector3?)null;
        // 2 interleaved stereo samples per frame
        var delaySamples = (int)((delaySeconds ?? Definition.Delay) * SampleRate) * 2;

        var sampleProvider = BuildTrackProvider(cachedSound, position, pitch, delaySamples);
        sampleProvider.Volume = volume;

        if (sampleProvider is SampleProvider3D spatial)
        {
            spatial.Range = range;
            spatial.DistanceVolumeCurve = distanceVolumeCurve;
            spatial.StereoMixCurve = stereoMixCurve;
        }

        // Added last: on a retrigger the event is already attached, and the mixing thread must never read a half-configured provider
        SampleProviders.Add(sampleProvider);
        return sampleProvider;
    }

    /// <summary>Gets the definition's pitch with an authored random offset applied.</summary>
    private protected float GetRandomizedPitch(float randomMin, float randomMax)
    {
        var pitch = Definition.Pitch;

        if (randomMin != 0f || randomMax != 0f)
        {
            pitch += float.Lerp(randomMin, randomMax, Random.NextSingle());
        }

        return Math.Clamp(pitch, 0.25f, 4f);
    }

    /// <summary>Gets the volume this play starts at: the caller's override or the definition's own volume.</summary>
    private protected float GetRandomizedVolume(float randomMin, float randomMax, string mixGroup)
    {
        var volume = VolumeOverride ?? Definition.Volume;

        if (randomMin != 0f || randomMax != 0f)
        {
            volume += float.Lerp(randomMin, randomMax, Random.NextSingle());
        }

        return Math.Clamp(volume, 0f, 1f) * VolumeScale * Mixer.Player.GetMixGroupVolume(mixGroup);
    }

    /// <summary>
    /// Queues background decodes for every track a <see cref="StartTrack"/> call could pick, and pre-builds
    /// the provider chain for the first of them.
    /// </summary>
    private protected void PrewarmTracks(string[] trackNames)
    {
        foreach (var trackName in trackNames)
        {
            Mixer.Player.SoundCache.GetSound(trackName, background: true);
        }

        if (trackNames.Length > 0)
        {
            PrewarmTrackProvider(Mixer.Player.SoundCache.GetSound(trackNames[0], background: true));
        }
    }

    /// <summary>
    /// Starts (or restarts, on a later retrigger) one child per entry in <paramref name="definitions"/>: builds
    /// each the first time and reuses the same instance afterwards instead of rebuilding its whole provider
    /// subtree from scratch every time. Null entries (unresolved definitions) are skipped.
    /// Call once per <see cref="DoStart"/> when a definition plays a fixed set of child events.
    /// </summary>
    /// <param name="definitions">The child definitions to start, by index.</param>
    /// <param name="beforeStart">
    /// Optional per-child customization (e.g. <see cref="DelayOverride"/>), invoked right after a child is
    /// built/resolved but before it starts. Not invoked again for a child already started on an earlier
    /// call - set values that must survive every retrigger directly on the child's definition instead.
    /// </param>
    protected void StartChildren(SoundEventDefinition?[] definitions, Action<SoundEvent, int>? beforeStart = null)
    {
        for (var i = 0; i < definitions.Length; i++)
        {
            var child = GetOrBuildChild(definitions, i);

            if (child != null)
            {
                beforeStart?.Invoke(child, i);
                StartAsChild(child);
            }
        }
    }

    /// <summary>
    /// Resolves child event names through the bank, in order, leaving a null where a name is unknown.
    /// Callers cache the result on <see cref="SoundEventDefinition.ChildDefinitions"/> so every instance
    /// and retrigger of the definition reuses the one resolution.
    /// </summary>
    private protected SoundEventDefinition?[] ResolveChildDefinitions(string[] names)
    {
        var definitions = new SoundEventDefinition?[names.Length];

        for (var i = 0; i < names.Length; i++)
        {
            definitions[i] = Mixer.Player.Bank.GetSoundEvent(names[i]);
        }

        return definitions;
    }

    /// <summary>
    /// Gets the instance for one child slot, building it the first time and reusing it on every later
    /// start, for the types that start one child at a time rather than all of them. Null when the slot
    /// holds no definition or its type is unsupported. Start it with <see cref="StartAsChild"/>.
    /// </summary>
    private protected SoundEvent? GetOrBuildChild(SoundEventDefinition?[] definitions, int index)
    {
        children ??= new SoundEvent?[definitions.Length];

        var definition = definitions[index];

        return definition == null ? null : children[index] ??= Build(definition);
    }

    /// <summary>
    /// Pre-builds everything this event would otherwise lazily create on its first start - child
    /// instances, track providers - and queues background decodes for every vsnd it could pick, so the
    /// first real play allocates nothing and reads warm samples. Called on pooled idle instances by
    /// <see cref="SoundEventPlayer.Cache(string)"/> at load/approach time; must not start anything.
    /// Types override this to warm the parts only they know about; the base does nothing.
    /// </summary>
    /// <param name="depth">Recursion depth, guarding against cyclic child references.</param>
    internal virtual void Prewarm(int depth)
    {
    }

    /// <summary>
    /// Builds (and recursively prewarms) one child instance per entry in <paramref name="definitions"/>,
    /// wired exactly like <see cref="StartAsChild"/> would on first start, but without starting anything.
    /// </summary>
    private protected void PrewarmChildren(SoundEventDefinition?[] definitions, int depth)
    {
        if (depth > MaxRecursionDepth)
        {
            return;
        }

        children ??= new SoundEvent?[definitions.Length];

        // The start-time lists fill with one entry per child (plus a possible own track provider);
        // pre-size them so the first start's Adds never grow a backing array
        var startCapacity = definitions.Length + 1;
        if (ChildSoundEvents.Capacity < startCapacity)
        {
            ChildSoundEvents.Capacity = startCapacity;
        }

        if (SampleProviders.Capacity < startCapacity)
        {
            SampleProviders.Capacity = startCapacity;
        }

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
                PrewarmChild(child, depth);
            }
        }
    }

    /// <summary>
    /// Wires a single child instance exactly like <see cref="StartAsChild"/> would on first start (which
    /// then recognizes it as already wired) and recursively prewarms it. For types that hold their child
    /// outside the shared child slots; <see cref="PrewarmChildren"/> uses this per slot.
    /// </summary>
    private protected void PrewarmChild(SoundEvent child, int depth)
    {
        if (child.SampleProvider is null)
        {
            child.Init(Mixer, SampleRate);
            child.OnSoundStart += ChildSoundStarted;
            child.OnSoundOver += ChildSoundOver;
        }

        child.SampleProvider.PrewarmMixNode();
        child.Prewarm(depth + 1);
    }

    /// <summary>
    /// Pre-creates the leaf provider chain <see cref="BuildTrackProvider"/> would otherwise build on the
    /// first start: the track source (bound to <paramref name="sound"/> until the first start rebinds it)
    /// and both the spatialized and unspatialized wrappers, since which one a play uses depends on
    /// whether it gets a position.
    /// </summary>
    private protected void PrewarmTrackProvider(CachedSound sound)
    {
        trackSource ??= new CachedSoundSampleProvider(sound);
        spatializedTrackSource ??= new SampleProvider3D(trackSource);
        unspatializedTrackSource ??= new SampleProvider2D(trackSource);

        // The wrappers are what gets added to this event's mix; the raw track source never is
        spatializedTrackSource.PrewarmMixNode();
        unspatializedTrackSource.PrewarmMixNode();

        if (SampleProviders.Capacity == 0)
        {
            SampleProviders.Capacity = 1;
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

    /// <summary>Marks the event as no longer audible, raises <see cref="OnSoundOver"/> and stops the event unless something in it is scheduled to sound again.</summary>
    protected virtual void OnFinished()
    {
        Playing = false;
        OnSoundOver?.Invoke(this);

        if (FadingOut)
        {
            // The fade ran to completion, finish the stop
            Stop();
            return;
        }

        if (!StayAliveAfterFinishing())
        {
            // Nothing in this tree can produce samples anymore, so leave the mixer's active set instead
            // of staying registered (and updated) forever. A genuinely looping track (baked-in loop
            // points) never gets here; for those types this only catches a mistakenly non-looping vsnd.
            Stop();
        }
    }

    /// <summary>
    /// Called when the event has run out of samples and is not fading out, to decide whether it stays in
    /// the mixer's active set. Types that reschedule themselves arm their next play here and return true.
    /// The default keeps the event alive only while a child still is - one waiting on its own retrigger.
    /// </summary>
    private protected virtual bool StayAliveAfterFinishing() => AnyChildStarted();

    /// <summary>Marks the event as audible and raises <see cref="OnSoundStart"/>.</summary>
    protected virtual void OnStarted()
    {
        Playing = true;
        OnSoundStart?.Invoke(this);
    }

    /// <summary>Updates spatialization and time-based behavior. Returns whether any sample provider is currently audible.</summary>
    public virtual bool Update(in ListenerState listener)
    {
        if (Started && !FadingOut && waitingForRetrigger && Stopwatch.GetTimestamp() >= retriggerTimestamp)
        {
            waitingForRetrigger = false;
            Start();
        }

        var anyPlaying = false;

        var occlusionTrace = Definition.OcclusionIntensity > 0f ? Mixer.Player.OcclusionTrace : null;

        if (occlusionTrace != null)
        {
            // Occlusion is smoothed, so it does not need a ray every frame: retrace ~10 times
            // a second, with a jittered interval so concurrent events spread across frames
            var now = Stopwatch.GetTimestamp();
            if (now < nextOcclusionTraceTimestamp)
            {
                occlusionTrace = null;
            }
            else
            {
                var interval = 0.08f + 0.04f * Random.NextSingle();
                nextOcclusionTraceTimestamp = now + (long)(interval * Stopwatch.Frequency);
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
                    spatialProvider.OcclusionTarget = occlusionTrace(listener.Position, spatialProvider.Position)
                        ? 1f - Definition.OcclusionIntensity
                        : 1f;
                }

                if (spatialProvider.Update(listener))
                {
                    anyPlaying = true;
                }
            }
        }

        foreach (var child in ChildSoundEvents)
        {
            if (child.Update(listener))
            {
                anyPlaying = true;
            }
        }

        return anyPlaying;
    }

    /// <summary>
    /// Resets per-play state so a pooled, fully stopped instance can be handed out by
    /// <see cref="SoundEventPlayer.Play"/> again as if freshly built - the allocation-free counterpart
    /// to building a new instance. Cascades to already-built child instances. Types with their own
    /// per-play state (first-interval waits, retrigger timers, separately held children) override this.
    /// Must only be called while the instance is idle (not <see cref="Started"/>).
    /// </summary>
    internal virtual void ResetForReplay()
    {
        FadingOut = false;
        wasInitialized = false;
        waitingForRetrigger = false;
        Position = null;
        PositionOffset = default;
        VolumeOverride = null;
        VolumeScale = 1f;
        DelayOverride = null;
        PlayingSoundFile = null;
        SampleProvider?.ResetFades();

        if (children != null)
        {
            foreach (var child in children)
            {
                child?.ResetForReplay();
            }
        }
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
            "csgo_mega" or "choreo_3d" => new SoundEventCSGOMega(definition),
            "citadel_default_2d" or "citadel_default_3d" or "citadel_ambient_3d" or "citadel_perspective_default"
                or "citadel_emitter_lod" or "citadel_emitter_obb" or "citadel_dialog" or "citadel_music"
                or "citadel_diagetic_music" or "citadel_ui_panner" or "citadel_weapons" or "citadel_footsteps"
                or "citadel_bullet_impact" or "citadel_bullet_whizby" or "citadel_hit_confirm" or "citadel_damage"
                or "citadel_health_effects" or "citadel_closest_point_on_segment"
                => new SoundEventCitadel(definition),
            "citadel_start_multi" => new SoundEventHLVRMulti(definition),
            "hlvr_default_3d" or "hlvr_2d_w_occlusion" or "src1_3d" or "src1_2d"
                or "hlvr_default_3d_on_aabb" or "hlvr_default_3d_xen_propagation" or "hlvr_lpf_3d" or "hlvr_2d_w_falloff"
                or "hlvr_ambient_rand_child" or "hlvr_ambient_rand_child_random_anim_time"
                => new SoundEventHLVRDefault(definition),
            "hlvr_update_vo_default" or "hlvr_update_vo_combine" or "hlvr_music_2d" or "hlvr_music_3d"
                => new SoundEventHLVRDefault(definition),
            "hlvr_gun_layers_3d" or "hlvr_player_gun_layers_3d" => new SoundEventHLVRGunLayers(definition),
            "hlvr_start_soundevent" => new SoundEventHLVRStartSoundEvent(definition),
            "hlvr_animate_soundevent" => new SoundEventHLVRMulti(definition),
            "hlvr_start_multi" or "hlvr_start_multi_quad" or "hlvr_start_multi_24" or "hlvr_start_multi_simple"
                or "hlvr_start_multi_aabb" or "hlvr_start_multi_bullet" or "hlvr_startup_start_multi"
                or "hlvr_music_start_multi_quad"
                => new SoundEventHLVRMulti(definition),
            "hlvr_start_multi_switch" => new SoundEventHLVRSwitch(definition),
            "hlvr_ambient_rand" => new SoundEventHLVRAmbientRand(definition),
            "hlvr_ambient_fixed_rotation" => new SoundEventHLVRAmbientFixedRotation(definition),
            "hlvr_ambient_fixed_rotation_multi_vsnd" => new SoundEventHLVRAmbientMultiVsnd(definition),
            "script_playrandom" => new SoundEventScriptedRandom(definition),
            "script_playlooping" => new SoundEventScriptedLoop(definition),
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
