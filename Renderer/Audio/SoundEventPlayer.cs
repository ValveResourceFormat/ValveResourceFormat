using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Plays sound events through an injected <see cref="IAudioDevice"/>.
/// Owns a mixing thread that continuously mixes active sounds and submits the samples to the device.
/// </summary>
public sealed class SoundEventPlayer : IDisposable
{
    /// <summary>Gets the decoded sound store.</summary>
    public SoundCache SoundCache { get; }

    /// <summary>Gets the bank of loaded sound event definitions.</summary>
    public SoundEventBank Bank { get; }

    /// <summary>Gets the bank of loaded scripted soundscape definitions (see <see cref="LoadSoundscapes"/>).</summary>
    public SoundscapeBank Soundscapes { get; }

    /// <summary>Gets the mixer output sample rate, taken from the device.</summary>
    public int SampleRate => device.SampleRate;

    private const int MixChunkFrames = 512;
    // Duration of the fade applied when the player is suspended/resumed (e.g. on focus loss/gain).
    private const float SuspendFadeSeconds = 0.12f;

    private float volume = 1f;

    // Read on the mixing thread, written from the game/UI thread; volatile so the change is seen promptly.
    private volatile float volumeMultiplier = 1f;
    private volatile bool suspended;
    private volatile bool mute;

    /// <summary>
    /// Gets or sets whether output is suspended, fading to silence and back so focus/tab changes don't snap
    /// the audio. Unlike <see cref="Mute"/>, sounds keep advancing in real time while suspended (just
    /// inaudible) so a sound triggered mid-suspend is at the correct position, not frame 0, once resumed.
    /// </summary>
    public bool Suspended
    {
        get => suspended;
        set => suspended = value;
    }

    /// <summary>Gets or sets whether the player is muted, independently of <see cref="Volume"/> and with no fade. While silent, the mixing thread skips decoding/mixing entirely instead of mixing to silence.</summary>
    public bool Mute
    {
        get => mute;
        set => mute = value;
    }

    /// <summary>
    /// Gets or sets the master output volume (0..1), applied to the final mix on top of per-sound and
    /// mix group volumes. Mapped onto the same perceptual curve as the per-sound volume controls.
    /// Safe to set live from any thread; the mixing thread picks it up on its next chunk.
    /// </summary>
    public float Volume
    {
        get => volume;
        set
        {
            value = Math.Clamp(value, 0f, 1f);
            if (value == volume)
            {
                return;
            }

            volume = value;
            volumeMultiplier = MathUtils.ToPerceptualVolume(value);
        }
    }

    private float positionalSharpness = 1f;

    /// <summary>
    /// Gets or sets how hard a positioned sound pulls towards the ear it is on.
    /// </summary>
    public float PositionalSharpness
    {
        get => positionalSharpness;
        set => positionalSharpness = Math.Clamp(value, 0.25f, 4f);
    }

    private float spectralCueStrength = 0.6f;

    /// <summary>
    /// Gets or sets how strongly a positioned sound is coloured by where it is vertically.
    /// </summary>
    public float SpectralCueStrength
    {
        get => spectralCueStrength;
        set => spectralCueStrength = Math.Clamp(value, 0f, 1f);
    }

    private readonly IFileLoader fileLoader;
    private readonly IAudioDevice device;
    private readonly ILogger logger;
    private readonly AudioMixer mixer;
    private readonly Thread mixingThread;
    private readonly Dictionary<string, SoundEvent> channels = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool stopping;

    /// <summary>
    /// Active instances per "limiter_*" group (see <see cref="RegisterLimiterGroup"/>), keyed by
    /// "limiter_event_name" when a definition sets it, otherwise its own name. Entries are appended in
    /// start order, so the front of a list is always the oldest active instance.
    /// </summary>
    private readonly Dictionary<string, List<SoundEvent>> limiterGroups = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the random source used for randomized sound event properties, shared by every event this
    /// player owns and reseeded in place with the play time on every <see cref="Play"/> - no allocation,
    /// and no dependence on process-wide random state. See <see cref="SoundRandom"/> on why it is
    /// unsynchronized despite both threads drawing from it.
    /// </summary>
    internal SoundRandom Random { get; } = new();

    /// <summary>
    /// Gets the volume multipliers per mix group ("mixgroup" in the sound event data, e.g. "Weapons", "Footsteps").
    /// A crude stand-in for the game's mix graph: groups without an entry play at full volume.
    /// Applies to sounds started after the change.
    /// </summary>
    public Dictionary<string, float> MixGroupVolume { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the fallback volume multiplier for mix groups without an explicit
    /// <see cref="MixGroupVolume"/> entry.
    /// Events without any mix group are not affected and play at authored volume.
    /// </summary>
    public float DefaultMixGroupVolume { get; set; } = 1f;

    /// <summary>
    /// Gets the volume multiplier for a mix group: its <see cref="MixGroupVolume"/> entry,
    /// <see cref="DefaultMixGroupVolume"/> for unknown groups, and 1 when the event has no group.
    /// </summary>
    public float GetMixGroupVolume(string mixGroup)
    {
        if (mixGroup.Length == 0)
        {
            return 1f;
        }

        return MixGroupVolume.TryGetValue(mixGroup, out var volume) ? volume : DefaultMixGroupVolume;
    }

    /// <summary>Creates a sound event player. Takes ownership of <paramref name="device"/> and starts the mixing thread immediately.</summary>
    public SoundEventPlayer(IFileLoader fileLoader, IAudioDevice device, ILogger? logger = null)
    {
        this.fileLoader = fileLoader;
        this.device = device;
        this.logger = logger ?? NullLogger.Instance;

        SoundCache = new SoundCache(fileLoader, device.SampleRate, device.Channels, this.logger);
        Bank = new SoundEventBank();
        Soundscapes = new SoundscapeBank(Bank);
        mixer = new AudioMixer(this);

        mixingThread = new Thread(MixingLoop)
        {
            IsBackground = true,
            Name = "VRF Sound Mixer",
            // The mix-ahead is small (~25 ms), so losing the CPU to the render thread for longer
            // than that underruns the device; audio pump threads conventionally run elevated
            Priority = ThreadPriority.Highest,
        };
        mixingThread.Start();

        Sound.Player = this;
    }

    private void MixingLoop()
    {
        var channelCount = device.Channels;
        var buffer = new float[MixChunkFrames * channelCount];

        // How far the suspend fade can move within a single chunk
        var fadeStep = (float)MixChunkFrames / (device.SampleRate * SuspendFadeSeconds);

        // Ramped 0..1 gain that follows the suspended flag; mixing-thread-only state.
        // Starts at zero so output always fades in from silence rather than snapping.
        var fadeGain = 0f;

        // How long a chunk represents, used to pace the loop when muted and skipping real work.
        var chunkMilliseconds = (int)(1000L * MixChunkFrames / device.SampleRate);

        while (!stopping)
        {
            // Nothing to hear: skip decoding/mixing rather than discard silence. The device gets nothing
            // (BufferedWaveProvider's ReadFully plays silence on its own when starved). Deliberately not
            // extended to a completed suspend fade - see Suspended, whose sounds must keep advancing.
            if (mute || volumeMultiplier == 0f)
            {
                Thread.Sleep(chunkMilliseconds);
                continue;
            }

            var mixStart = Stopwatch.GetTimestamp();

            mixer.Read(buffer, 0, buffer.Length);

            var target = suspended ? 0f : 1f;
            var startGain = fadeGain;
            var endGain = startGain < target
                ? Math.Min(target, startGain + fadeStep)
                : Math.Max(target, startGain - fadeStep);
            fadeGain = endGain;

            var master = volumeMultiplier;
            var startMul = master * startGain;
            var endMul = master * endGain;

            if (startMul != 1f || endMul != 1f)
            {
                // Interpolate the gain across the chunk so a fade (or master volume change) does not step;
                // the last frame must land exactly on endMul so chunks join without a gain step
                var frames = buffer.Length / channelCount;
                var lastFrame = Math.Max(frames - 1, 1);
                var index = 0;

                for (var frame = 0; frame < frames; frame++)
                {
                    var mul = float.Lerp(startMul, endMul, (float)frame / lastFrame);

                    for (var ch = 0; ch < channelCount; ch++)
                    {
                        buffer[index++] *= mul;
                    }
                }
            }

            // Measured before the submit, which blocks until the device drains: including that would
            // report how long the thread slept, not what mixing a chunk costs.
            RecordMixTime((float)Stopwatch.GetElapsedTime(mixStart).TotalMilliseconds, chunkMilliseconds);

            device.SubmitSamples(buffer);
        }
    }

    private volatile float mixMilliseconds;
    private volatile float mixDutyCycle;

    /// <summary>Gets the recent average time spent mixing one chunk, in milliseconds.</summary>
    public float MixMilliseconds => mixMilliseconds;

    /// <summary>
    /// Gets the share of real time the mixing thread spends mixing rather than waiting on the device,
    /// 0 to 1 - effectively how much of one core the audio costs.
    /// </summary>
    public float MixDutyCycle => mixDutyCycle;

    private void RecordMixTime(float elapsedMilliseconds, int chunkMilliseconds)
    {
        // Smoothed: per-chunk times swing with how many sounds happen to be audible, and the display
        // reads this at an unrelated rate
        const float Smoothing = 0.05f;

        mixMilliseconds = float.Lerp(mixMilliseconds, elapsedMilliseconds, Smoothing);
        mixDutyCycle = chunkMilliseconds > 0 ? mixMilliseconds / chunkMilliseconds : 0f;
    }

    /// <summary>
    /// Conventional path for an addon/workshop map's own extra sound events. Unlike the rest of a base
    /// game's content, an addon's own files are not necessarily reachable from
    /// "soundevents/soundevents_manifest.vrman" (that manifest ships with the base game, not the addon),
    /// so this is always attempted directly in <see cref="LoadSoundEvents"/> regardless of what is loaded -
    /// most content (anything that is not an addon) simply does not have the file.
    /// </summary>
    private const string AddonSoundEventsFile = "soundevents/soundevents_addon.vsndevts";

    /// <summary>
    /// Loads sound event definitions from all soundevent files listed in the game's soundevents manifest,
    /// plus <see cref="AddonSoundEventsFile"/> when present. An optional filter matches against the file
    /// name (e.g. "game_sounds").
    /// </summary>
    public void LoadSoundEvents(string filter = "")
    {
        var stopwatch = Stopwatch.StartNew();

        foreach (var soundEventsFile in GetSoundEventFiles())
        {
            if (filter.Length == 0 || soundEventsFile.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                LoadSoundEventsFile(soundEventsFile);
            }
        }

        if (filter.Length == 0 || AddonSoundEventsFile.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            LoadSoundEventsFile(AddonSoundEventsFile, optional: true);
        }

        stopwatch.Stop();
        logger.LogInformation("Loaded {Count} sound events in {ElapsedMs} ms", Bank.Count, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Loads sound event definitions from a single soundevent (vsndevts) file. When <paramref name="optional"/>
    /// is set, a missing file is skipped quietly instead of logging a warning (see <see cref="AddonSoundEventsFile"/>).
    /// </summary>
    public void LoadSoundEventsFile(string fileName, bool optional = false)
    {
        using var soundEventsFile = fileLoader.LoadFileCompiled(fileName);
        if (soundEventsFile?.DataBlock == null)
        {
            if (!optional)
            {
                logger.LogWarning("Could not load sound events file {FileName}", fileName);
            }

            return;
        }

        if (soundEventsFile.DataBlock is not BinaryKV3)
        {
            logger.LogWarning(
                "Sound events file {FileName} has unexpected data block type {BlockType}, skipping",
                fileName,
                soundEventsFile.DataBlock.GetType().Name);
            return;
        }

        Bank.AddSoundEvents(soundEventsFile.DataBlock.AsKeyValueCollection());
    }

    /// <summary>
    /// Loads scripted soundscape definitions (see <see cref="SoundscapeBank"/>) from the game's
    /// "scripts/soundscapes_manifest.txt", when present. Most modern titles only use the newer
    /// single-sound-event soundscapes (see <see cref="AddSoundscape"/>), which need no separate loading
    /// step, so a missing manifest is not an error.
    /// </summary>
    public void LoadSoundscapes()
    {
        var manifest = ReadKeyValues1("scripts/soundscapes_manifest.txt");

        if (manifest == null || !manifest.TryGetValue("soundscapes_manifest", out var fileList))
        {
            return;
        }

        foreach (var entry in fileList)
        {
            if (!entry.Key.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = (string)entry.Value;

            if (string.IsNullOrEmpty(fileName))
            {
                continue;
            }

            if (fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var script = ReadKeyValues1(fileName);

                if (script != null)
                {
                    Soundscapes.AddSoundscapes(script);
                }
            }
            else
            {
                // Additional sound events used only by the soundscape script (e.g. an ambience .vsndevts)
                // that may not otherwise be reachable from soundevents_manifest.vrman
                LoadSoundEventsFile(fileName);
            }
        }
    }

    /// <summary>
    /// Reads and parses a loose KeyValues1 ("VDF") text file, or null when it does not exist or fails to
    /// parse. The raw content is wrapped in a synthetic root object before parsing: soundscape scripts
    /// author several sibling top-level blocks with no single enclosing key, which the deserializer
    /// otherwise silently folds into (and loses the name of) the first block instead of treating as
    /// siblings. Hand-authored script files do show up with encoding quirks or syntax errors, so a
    /// failure here is logged and skipped rather than allowed to take down the whole load.
    /// </summary>
    // Shared across all script files: creating a serializer spins up ValveKeyValue's reflection machinery
    private static readonly KVSerializer KeyValues1Serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

    private KVObject? ReadKeyValues1(string file)
    {
        using var stream = fileLoader.GetFileStream(file);

        if (stream == null)
        {
            return null;
        }

        try
        {
            using var wrapped = new MemoryStream();
            wrapped.Write("\"__root\"\n{\n"u8);
            stream.CopyTo(wrapped);
            wrapped.Write("\n}\n"u8);
            wrapped.Position = 0;

            return KeyValues1Serializer.Deserialize(wrapped);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse KeyValues1 file {File}", file);
            return null;
        }
    }

    /// <summary>
    /// Plays a sound event by name. Allocation-free once a definition has played before: instances are
    /// pooled per definition and reused (with their whole provider tree) once fully stopped.
    /// </summary>
    /// <param name="soundEventName">Name of the sound event, e.g. "Default.StepLeft".</param>
    /// <param name="position">World position of the sound, or null for non-spatialized playback.</param>
    /// <param name="channel">Optional channel name (e.g. "player"). Playing on a channel stops whatever was playing on that channel before.</param>
    /// <param name="volume">Optional programmatic volume, replacing the definition's volume property.</param>
    /// <param name="volumeScale">Multiplier applied on top of whatever volume the event ends up at, and to its children.</param>
    /// <returns>
    /// A handle to the playing sound, or null when the event is unknown, its type is unsupported, or it
    /// produced nothing (empty track list, dropped by its limiter). The handle is only meaningful while
    /// the sound is <see cref="SoundEvent.Started"/>: once it stops, the instance returns to its
    /// definition's pool and a later play of the same event may hand the same instance out again.
    /// </returns>
    public SoundEvent? Play(string soundEventName, Vector3? position = null, string? channel = null, float? volume = null,
        float volumeScale = 1f)
    {
        var definition = Bank.GetSoundEvent(soundEventName);
        if (definition == null)
        {
            logger.LogUniqueWarning("Unknown sound event {SoundEventName}", soundEventName);
            return null;
        }

        if (IsBlocked(definition))
        {
            return null;
        }

        var soundEvent = RentSoundEvent(definition);
        if (soundEvent == null)
        {
            // Keyed on the type alone: one warning per unsupported type, not per event that uses it
            logger.LogUniqueWarningFor([definition.Type], "Unsupported sound event type {Type} for {SoundEventName}",
                definition.Type, soundEventName);
            return null;
        }

        // Seed with the play time so each play draws a fresh deterministic sequence
        Random.Reseed(Stopwatch.GetTimestamp());

        soundEvent.Position = position;
        soundEvent.VolumeOverride = volume;
        soundEvent.VolumeScale = volumeScale;

        if (channel != null)
        {
            StopChannel(channel);
        }

        if (!soundEvent.Start())
        {
            // Inert: nothing in the event could produce samples now or later (no tracks, limiter drop).
            // It never reached the mixer, so no OnStop will fire - hand it straight back to the pool.
            ReturnToPool(soundEvent);
            return null;
        }

        if (channel != null)
        {
            channels[channel] = soundEvent;
        }

        return soundEvent;
    }

    /// <summary>
    /// Gets a ready-to-start instance for the definition: a pooled one when available (reset as if
    /// freshly built), otherwise a newly built one with its one-time wiring (mixer registration,
    /// pool-return hook) done here so later plays skip it. Null when the event type is unsupported.
    /// </summary>
    private SoundEvent? RentSoundEvent(SoundEventDefinition definition)
    {
        var pool = definition.IdlePool;

        if (pool != null)
        {
            SoundEvent? pooled = null;

            lock (pool)
            {
                var last = pool.Count - 1;
                if (last >= 0)
                {
                    pooled = pool[last];
                    pool.RemoveAt(last);
                }
            }

            if (pooled != null)
            {
                pooled.ResetForReplay();
                return pooled;
            }
        }

        var soundEvent = SoundEvent.Build(definition);
        if (soundEvent == null)
        {
            return null;
        }

        // Created before the pool-return hook is subscribed, so the hook always sees it non-null.
        // Only ever assigned on this (game) thread - Play is not called from the mixing thread.
        definition.IdlePool ??= [];

        soundEvent.Init(mixer, SampleRate);
        mixer.Register(soundEvent);

        // Subscribed after Register so the mixer detaches the stopping event before it becomes rentable
        soundEvent.OnStop += ReturnToPool;

        return soundEvent;
    }

    /// <summary>
    /// Returns a fully stopped instance to its definition's pool. Runs on the mixing thread when a
    /// sound runs dry mid-read, and on the game thread for explicit stops and inert starts.
    /// </summary>
    private void ReturnToPool(SoundEvent soundEvent)
    {
        var pool = soundEvent.Definition.IdlePool;

        if (pool == null)
        {
            return;
        }

        lock (pool)
        {
            pool.Add(soundEvent);
        }
    }

    /// <summary>
    /// Pre-warms a sound event so its first real play does no lazy work on the frame: builds the full
    /// instance tree (event instances, providers, child wiring) into the definition's pool, and queues
    /// background decodes for every vsnd the tree could pick. Also safe to call again while approaching
    /// an already-warm event - the instance work is a no-op and the decode queueing just refreshes the
    /// sounds' cache eviction age. Returns immediately; sounds that want to play right now take priority
    /// over the queued decodes. Unknown events and unsupported types are ignored.
    /// </summary>
    public void Cache(string soundEventName)
    {
        var definition = Bank.GetSoundEvent(soundEventName);
        if (definition == null)
        {
            return;
        }

        // Reuse the idle pooled instance when there is one: Prewarm re-queues its tree's decodes
        // (refreshing their eviction age) and fills in anything not built yet
        var soundEvent = RentSoundEvent(definition);
        if (soundEvent == null)
        {
            return;
        }

        soundEvent.SampleProvider.PrewarmMixNode();
        soundEvent.Prewarm(depth: 0);
        ReturnToPool(soundEvent);
    }

    /// <summary>Implements "block_matching_events": the same event cannot be played again within its "block_duration".</summary>
    private static bool IsBlocked(SoundEventDefinition definition)
    {
        if (!definition.BlockMatchingEvents || definition.BlockDuration <= 0f)
        {
            return false;
        }

        var now = Stopwatch.GetTimestamp();

        if (definition.LastPlayedTimestamp != 0
            && (now - definition.LastPlayedTimestamp) < (double)definition.BlockDuration * Stopwatch.Frequency)
        {
            return true;
        }

        definition.LastPlayedTimestamp = now;
        return false;
    }

    /// <summary>Picks a random track index out of <paramref name="trackCount"/>, never repeating the previously picked track for the same sound event definition.</summary>
    internal int PickTrack(SoundEventDefinition definition, int trackCount)
    {
        if (trackCount <= 1)
        {
            return 0;
        }

        int index;
        var last = definition.LastTrackIndex;

        if (last >= 0 && last < trackCount)
        {
            // Pick from the remaining tracks and skip over the last one
            index = Random.Next(trackCount - 1);
            if (index >= last)
            {
                index++;
            }
        }
        else
        {
            index = Random.Next(trackCount);
        }

        definition.LastTrackIndex = index;
        return index;
    }

    /// <summary>
    /// Gets how many instances are currently active under a "limiter_*" group key (see
    /// <see cref="RegisterLimiterGroup"/>), for a leaf event to compare against its own "limiter_max"
    /// before starting.
    /// </summary>
    internal int GetLimiterCount(string limiterKey)
    {
        lock (limiterGroups)
        {
            return limiterGroups.TryGetValue(limiterKey, out var group) ? group.Count : 0;
        }
    }

    /// <summary>Stops the longest-running active instance in a "limiter_*" group ("limiter_stop_oldest"), if any.</summary>
    internal void StopOldestLimiterInstance(string limiterKey)
    {
        SoundEvent? oldest = null;

        lock (limiterGroups)
        {
            if (limiterGroups.TryGetValue(limiterKey, out var group) && group.Count > 0)
            {
                oldest = group[0];
            }
        }

        // Stopped outside the lock: Stop cascades into mixer/provider locks the mixing thread takes before
        // firing the OnStop hook that locks limiterGroups (and removes the instance), so holding it here
        // would deadlock. A concurrent stop of the same instance is fine, Stop is idempotent.
        oldest?.Stop();
    }

    /// <summary>
    /// Adds <paramref name="soundEvent"/> to a "limiter_*" group for as long as it stays started, so
    /// concurrent instances of a "limiter_on" leaf event (or several sharing one "limiter_event_name")
    /// can be counted and capped. Call once per event instance (e.g. guarded by a flag set on first
    /// play) - <see cref="SoundEvent.OnStart"/>/<see cref="SoundEvent.OnStop"/> already fire once per
    /// start/stop cycle on their own, including across retriggers of a reused child instance.
    /// The hooks lock the registry: stops (and thus group removal) can come from the mixing thread.
    /// </summary>
    internal void RegisterLimiterGroup(SoundEvent soundEvent, string limiterKey)
    {
        soundEvent.OnStart += _ =>
        {
            lock (limiterGroups)
            {
                if (!limiterGroups.TryGetValue(limiterKey, out var group))
                {
                    limiterGroups[limiterKey] = group = [];
                }

                group.Add(soundEvent);
            }
        };

        soundEvent.OnStop += _ =>
        {
            lock (limiterGroups)
            {
                if (limiterGroups.TryGetValue(limiterKey, out var group))
                {
                    group.Remove(soundEvent);
                }
            }
        };
    }

    /// <summary>Stops the sound currently playing on the given channel, if any.</summary>
    public void StopChannel(string channel)
    {
        if (channels.Remove(channel, out var soundEvent))
        {
            soundEvent.Stop();
        }
    }

    /// <summary>
    /// Collects the position and vsnd name of every audible positioned sound into <paramref name="positioned"/>,
    /// and the vsnd name of every audible non-positioned (2D) sound into <paramref name="flat"/>.
    /// </summary>
    public void CollectDebugSounds(List<(Vector3 Position, string Text)> positioned, List<string> flat) => mixer.CollectDebugSounds(positioned, flat);

    /// <summary>
    /// Optional line-of-sight test for sound occlusion: given the listener position and a sound position,
    /// returns true when solid geometry blocks the segment. When set, events with an "occlusion_intensity"
    /// are attenuated while blocked (e.g. a distant moped behind buildings); when null nothing is occluded.
    /// </summary>
    public Func<Vector3, Vector3, bool>? OcclusionTrace { get; set; }

    /// <summary>
    /// Whether nothing solid stands between the listener and a soundscape entity. An env_soundscape only
    /// claims the listener when it can see them, which is what keeps the one across the wall (or the floor)
    /// from taking over the room they are actually standing in. Without an <see cref="OcclusionTrace"/> to
    /// ask there is nothing to test against, and every region counts as visible.
    /// </summary>
    private bool HasLineOfSight(Vector3 listenerPosition, Vector3 position)
    {
        return OcclusionTrace == null || !OcclusionTrace(listenerPosition, position);
    }

    /// <summary>
    /// Updates listener position and per-frame sound event logic, and claims <see cref="Sound.Player"/> as
    /// the active global listener. Call once per frame, only for the viewer currently active/visible -
    /// calling this from every open viewer's own renderer, active or not, would have each one stomp on
    /// whichever is the current global listener.
    /// </summary>
    public void Update(Camera camera)
    {
        Sound.Player = this;
        mixer.Update(camera.Location, camera.Forward, camera.Right, camera.Up);
        UpdateSoundscape(camera.Location);
        ReportStats();
    }

    /// <summary>
    /// Publishes what the audio threads did to the active <see cref="PerfStats"/>. Counted here once a
    /// frame rather than accumulated as it happens: these are snapshots of state owned by the mixing and
    /// decode threads, and the counters reset every frame, so one assignment per frame is the whole tally.
    /// </summary>
    private void ReportStats()
    {
        var perfStats = PerfStats.Active;

        perfStats.Count(Counter.SoundCacheMegabytes, (int)(SoundCache.CachedBytes / (1024 * 1024)));
        perfStats.Count(Counter.SoundDecodeQueue, SoundCache.PendingDecodes);

        if (perfStats.Timings.Capture)
        {
            // The mixing thread runs on its own clock, so it is reported rather than timed
            perfStats.Timings.SetAsyncRow("Sound Mixing Thread", mixMilliseconds,
                mixDutyCycle.ToString("P0", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// A soundscape region (env_soundscape): while the listener is within <paramref name="Radius"/>
    /// of <paramref name="Position"/> and can see it, <paramref name="Name"/> plays as the ambient bed -
    /// either directly as a single sound event (see <see cref="AddSoundscape"/>), or, when
    /// <paramref name="Scripted"/>, as every sound event a <see cref="SoundscapeBank"/> scripted
    /// soundscape resolves to (see <see cref="AddScriptedSoundscape"/>).
    /// A negative radius means the region has no range limit at all.
    /// </summary>
    public readonly record struct Soundscape(Vector3 Position, float Radius, string Name, bool Scripted)
    {
        /// <summary>Gets whether this region covers the whole map ("radius" authored as -1).</summary>
        public bool Unlimited => Radius < 0f;

        /// <summary>Gets whether a listener <paramref name="distance"/> away is within range of this region.</summary>
        public bool InRange(float distance) => Unlimited || distance < Radius;
    }

    private readonly List<Soundscape> soundscapes = [];
    private readonly HashSet<string> warmedSoundscapes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SoundEvent> activeSoundscapeEvents = [];
    private string? activeSoundscapeName;
    private bool activeSoundscapeScripted;
    private long nextSoundscapeUpdateTimestamp;

    // How often the listener's soundscape is re-picked, in seconds.
    private const float SoundscapeUpdateInterval = 0.25f;

    // How far outside a soundscape's radius its audio starts decoding. At run speed (250 u/s) this
    // buys the background decoder a few seconds before the soundscape can actually trigger.
    private const float SoundscapePrecacheMargin = 1024f;

    // Softer/longer than the ~1 second default fade-out: entering a soundscape (especially from total
    // silence, e.g. right after load) should ease in rather than jump straight to full volume.
    private const float SoundscapeFadeInSeconds = 2.5f;

    /// <summary>
    /// Registers a soundscape region playing a single sound event directly (a modern
    /// <c>env_soundscape</c> with "enablesoundevent" set). The closest in-range soundscape becomes the
    /// active ambient during <see cref="Update"/>; entering another region crossfades by stopping the
    /// previous event(s).
    /// </summary>
    /// <param name="position">Where the region is centered.</param>
    /// <param name="radius">How far it reaches, or a negative value for a region that covers the whole map.</param>
    /// <param name="soundEventName">The sound event to play as the ambient bed.</param>
    public void AddSoundscape(Vector3 position, float radius, string soundEventName)
    {
        if (radius != 0f && !string.IsNullOrEmpty(soundEventName))
        {
            Cache(soundEventName);
            soundscapes.Add(new Soundscape(position, radius, soundEventName, Scripted: false));
        }
    }

    /// <summary>
    /// Registers a soundscape region playing a "scripted" soundscape (a classic <c>env_soundscape</c>
    /// without "enablesoundevent" set): every sound event <paramref name="soundscapeName"/> resolves to,
    /// via <see cref="Soundscapes"/>, starts together when the region becomes active. Requires
    /// <see cref="LoadSoundscapes"/> to have been called first; an unknown name is silently ignored.
    /// </summary>
    /// <param name="position">Where the region is centered.</param>
    /// <param name="radius">How far it reaches, or a negative value for a region that covers the whole map.</param>
    /// <param name="soundscapeName">Name of the scripted soundscape to play.</param>
    public void AddScriptedSoundscape(Vector3 position, float radius, string soundscapeName)
    {
        if (radius != 0f && !string.IsNullOrEmpty(soundscapeName))
        {
            CacheScripted(soundscapeName);
            soundscapes.Add(new Soundscape(position, radius, soundscapeName, Scripted: true));
        }
    }

    private void CacheScripted(string soundscapeName)
    {
        var events = Soundscapes.GetSoundEvents(soundscapeName);

        if (events == null)
        {
            return;
        }

        foreach (var soundscapeEvent in events)
        {
            Cache(soundscapeEvent.Name);
        }
    }

    private void UpdateSoundscape(Vector3 listenerPosition)
    {
        // The engine re-picks the listener's soundscape on a think rather than every frame, and one pass
        // here walks every region in the map and traces to the ones in range of the listener
        var now = Stopwatch.GetTimestamp();

        if (now < nextSoundscapeUpdateTimestamp)
        {
            return;
        }

        nextSoundscapeUpdateTimestamp = now + (long)(SoundscapeUpdateInterval * Stopwatch.Frequency);

        Soundscape? closest = null;
        var closestDistance = float.MaxValue;

        foreach (var soundscape in soundscapes)
        {
            var distance = Vector3.Distance(soundscape.Position, listenerPosition);

            if ((soundscape.Unlimited || distance < soundscape.Radius + SoundscapePrecacheMargin)
                && warmedSoundscapes.Add(soundscape.Name))
            {
                // Approaching: re-queue the decode in case the load-time precache was evicted, so entering
                // the radius plays warm. Already-cached sounds just get their eviction age refreshed.
                if (soundscape.Scripted)
                {
                    CacheScripted(soundscape.Name);
                }
                else
                {
                    Cache(soundscape.Name);
                }
            }

            if (distance < closestDistance && soundscape.InRange(distance) && HasLineOfSight(listenerPosition, soundscape.Position))
            {
                closest = soundscape;
                closestDistance = distance;
            }
        }

        if (closest == null)
        {
            // Nothing in range to take over. A soundscape is not a trigger volume the ambience stops at
            // the edge of - the last one to claim the listener keeps playing until another one does, so
            // walking out of a radius (or losing sight of the entity) does not drop the map into silence.
            return;
        }

        // Compare by name (not handle): an active soundscape with no event still started (e.g. every
        // one-shot finished without retriggering, or every event failed to resolve/play) no longer
        // counts as active, so re-entering the area retries it instead of staying silent forever.
        var currentStillActive = activeSoundscapeEvents.Exists(static e => e.Started);

        if (currentStillActive
            && activeSoundscapeScripted == closest.Value.Scripted
            && string.Equals(activeSoundscapeName, closest.Value.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Crossfade: the outgoing ambient fades along its curve while the new one starts underneath
        FadeOutActiveSoundscape();
        activeSoundscapeName = closest.Value.Name;
        activeSoundscapeScripted = closest.Value.Scripted;

        if (closest.Value.Scripted)
        {
            var events = Soundscapes.GetSoundEvents(closest.Value.Name);

            if (events != null)
            {
                foreach (var soundscapeEvent in events)
                {
                    // Soundscapes are the listener's ambient bed, play them unspatialized
                    var soundEvent = Play(soundscapeEvent.Name, volumeScale: soundscapeEvent.Volume);

                    if (soundEvent != null)
                    {
                        soundEvent.FadeIn(SoundscapeFadeInSeconds);
                        activeSoundscapeEvents.Add(soundEvent);
                    }
                }
            }
        }
        else
        {
            var soundEvent = Play(closest.Value.Name);

            if (soundEvent != null)
            {
                //soundEvent.FadeIn(SoundscapeFadeInSeconds);
                activeSoundscapeEvents.Add(soundEvent);
            }
        }
    }

    private void FadeOutActiveSoundscape()
    {
        foreach (var soundEvent in activeSoundscapeEvents)
        {
            soundEvent.FadeOutAndStop();
        }

        activeSoundscapeEvents.Clear();
    }

    private IEnumerable<string> GetSoundEventFiles()
    {
        var visitedManifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return CollectSoundEventFiles("soundevents/soundevents_manifest.vrman", visitedManifests);
    }

    /// <summary>
    /// Manifests can reference other manifests (e.g. a top-level .vrman listing per-game .vrman
    /// files, which in turn list the actual .vsndevts files), so entries are recursed into rather
    /// than assumed to all be sound event files. Guards against cycles with <paramref name="visitedManifests"/>.
    /// </summary>
    private IEnumerable<string> CollectSoundEventFiles(string manifestFileName, HashSet<string> visitedManifests)
    {
        if (!visitedManifests.Add(manifestFileName))
        {
            yield break;
        }

        using var manifestResource = fileLoader.LoadFileCompiled(manifestFileName);

        if (manifestResource?.DataBlock is not ResourceManifest manifest)
        {
            yield break;
        }

        foreach (var resourceGroup in manifest.Resources)
        {
            foreach (var fileName in resourceGroup)
            {
                if (fileName.EndsWith(".vrman", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var nestedFile in CollectSoundEventFiles(fileName, visitedManifests))
                    {
                        yield return nestedFile;
                    }
                }
                else
                {
                    yield return fileName;
                }
            }
        }
    }

    /// <summary>Stops the mixing thread and disposes the mixer and the audio device.</summary>
    public void Dispose()
    {
        if (Sound.Player == this)
        {
            Sound.Player = null;
        }

        stopping = true;

        // Dispose the device first: it unblocks a SubmitSamples call the mixing thread may be
        // parked in, so the join below can actually succeed. Only tear down the components the
        // thread touches after it has exited.
        device.Dispose();

        if (!mixingThread.Join(TimeSpan.FromSeconds(1)))
        {
            logger.LogWarning("Sound mixing thread did not stop in time");
        }

        mixer.Dispose();
        SoundCache.Dispose();
    }
}
