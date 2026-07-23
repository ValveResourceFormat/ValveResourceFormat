using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    private readonly IFileLoader fileLoader;
    private readonly IAudioDevice device;
    private readonly ILogger logger;
    private readonly AudioMixer mixer;
    private readonly Thread mixingThread;
    private readonly Dictionary<string, SoundEvent> channels = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool stopping;

    /// <summary>
    /// Gets the random source used for randomized sound event properties.
    /// Reseeded in place with the play time on every <see cref="Play"/> - no allocation, no global shared random state.
    /// </summary>
    internal SoundRandom Random { get; } = new();

    /// <summary>
    /// Gets the volume multipliers per mix group ("mixgroup" in the sound event data, e.g. "Weapons", "Footsteps").
    /// A crude stand-in for the game's mix graph: groups without an entry play at full volume.
    /// Applies to sounds started after the change.
    /// </summary>
    public Dictionary<string, float> MixGroupVolumes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the fallback volume multiplier for mix groups without an explicit
    /// <see cref="MixGroupVolumes"/> entry.
    /// Events without any mix group are not affected and play at authored volume.
    /// </summary>
    public float DefaultMixGroupVolume { get; set; } = 1f;

    /// <summary>
    /// Gets the volume multiplier for a mix group: its <see cref="MixGroupVolumes"/> entry,
    /// <see cref="DefaultMixGroupVolume"/> for unknown groups, and 1 when the event has no group.
    /// </summary>
    public float GetMixGroupVolume(string mixGroup)
    {
        if (mixGroup.Length == 0)
        {
            return 1f;
        }

        return MixGroupVolumes.TryGetValue(mixGroup, out var volume) ? volume : DefaultMixGroupVolume;
    }

    /// <summary>Creates a sound event player. Takes ownership of <paramref name="device"/> and starts the mixing thread immediately.</summary>
    public SoundEventPlayer(IFileLoader fileLoader, IAudioDevice device, ILogger? logger = null)
    {
        this.fileLoader = fileLoader;
        this.device = device;
        this.logger = logger ?? NullLogger.Instance;

        SoundCache = new SoundCache(fileLoader, device.SampleRate, device.Channels, this.logger);
        Bank = new SoundEventBank();
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
            // Nothing to hear: skip decoding/mixing instead of just discarding silence. The device
            // gets nothing (BufferedWaveProvider's ReadFully plays silence on its own when starved).
            // Deliberately NOT extended to "suspended && fadeGain == 0f": Suspended is driven by things
            // like tab-switching, which happens far more often and for far less predictable durations than
            // an explicit mute - a sound triggered while suspended must keep advancing in real time so it
            // doesn't play back "late" (from frame 0) whenever the tab becomes active again.
            if (mute || volumeMultiplier == 0f)
            {
                Thread.Sleep(chunkMilliseconds);
                continue;
            }

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

            device.SubmitSamples(buffer);
        }
    }

    /// <summary>
    /// Loads sound event definitions from all soundevent files listed in the game's soundevents manifest.
    /// An optional filter matches against the file name (e.g. "game_sounds").
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

        stopwatch.Stop();
        logger.LogInformation("Loaded {Count} sound events in {ElapsedMs} ms", Bank.Count, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Loads sound event definitions from a single soundevent (vsndevts) file.</summary>
    public void LoadSoundEventsFile(string fileName)
    {
        using var soundEventsFile = fileLoader.LoadFileCompiled(fileName);
        if (soundEventsFile?.DataBlock == null)
        {
            logger.LogWarning("Could not load sound events file {FileName}", fileName);
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
    /// Plays a sound event by name.
    /// </summary>
    /// <param name="soundEventName">Name of the sound event, e.g. "Default.StepLeft".</param>
    /// <param name="position">World position of the sound, or null for non-spatialized playback.</param>
    /// <param name="channel">Optional channel name (e.g. "player"). Playing on a channel stops whatever was playing on that channel before.</param>
    /// <param name="volume">Optional programmatic volume, replacing the definition's volume property.</param>
    /// <returns>A handle to the playing sound, or null when the event is unknown or its type is unsupported.</returns>
    public SoundEvent? Play(string soundEventName, Vector3? position = null, string? channel = null, float? volume = null)
    {
        var definition = Bank.GetSoundEvent(soundEventName);
        if (definition == null)
        {
            logger.LogWarning("Unknown sound event {SoundEventName}", soundEventName);
            return null;
        }

        if (IsBlocked(definition))
        {
            return null;
        }

        var soundEvent = SoundEvent.Build(definition);
        if (soundEvent == null)
        {
            logger.LogWarning("Unsupported sound event type {Type} for {SoundEventName}",
                definition.Type, soundEventName);
            return null;
        }

        // Seed with the play time so each play draws a fresh deterministic sequence
        Random.Reseed(Stopwatch.GetTimestamp());

        soundEvent.Position = position;
        soundEvent.VolumeOverride = volume;
        soundEvent.Init(mixer, SampleRate);
        mixer.Register(soundEvent);

        if (channel != null)
        {
            StopChannel(channel);
            channels[channel] = soundEvent;
        }

        soundEvent.Start();
        return soundEvent;
    }

    /// <summary>
    /// Queues background decodes for every vsnd a sound event could play - all random track variants and any
    /// child events. Returns immediately; sounds that want to play right now take priority over the queue.
    /// Unknown events are ignored.
    /// </summary>
    public void Cache(string soundEventName)
    {
        var definition = Bank.GetSoundEvent(soundEventName);
        if (definition != null)
        {
            Cache(definition, depth: 0);
        }
    }

    private void Cache(SoundEventDefinition definition, int depth)
    {
        if (depth > 8)
        {
            // Matches the base resolution depth limit, guarding against cyclic child references
            return;
        }

        // Only "csgo_mega"'s key names are understood here - pre-caching is a heuristic optimization,
        // not authoritative playback logic, so it's fine for other event types to just not pre-warm yet.
        var data = definition.Data;

        foreach (var track in SoundEvent.GetStringOrArrayProperty(data, "vsnd_files_track_01"))
        {
            SoundCache.GetSound(track, background: true);
        }

        if (data.GetBooleanProperty("enable_child_events"))
        {
            foreach (var childName in SoundEvent.GetStringOrArrayProperty(data, "soundevent_01"))
            {
                var child = Bank.GetSoundEvent(childName);
                if (child != null)
                {
                    Cache(child, depth + 1);
                }
            }
        }
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

        var index = 0;
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

    /// <summary>Stops the sound currently playing on the given channel, if any.</summary>
    public void StopChannel(string channel)
    {
        if (channels.Remove(channel, out var soundEvent))
        {
            soundEvent.Stop();
        }
    }

    /// <summary>Collects the position and vsnd name of every audible positioned sound.</summary>
    public void CollectDebugSounds(List<(Vector3 Position, string Text)> results) => mixer.CollectDebugSounds(results);

    /// <summary>
    /// Optional line-of-sight test for sound occlusion: given the listener position and a sound position,
    /// returns true when solid geometry blocks the segment. When set, events with an "occlusion_intensity"
    /// are attenuated while blocked (e.g. a distant moped behind buildings); when null nothing is occluded.
    /// </summary>
    public Func<Vector3, Vector3, bool>? OcclusionTrace { get; set; }

    /// <summary>
    /// Updates listener position and per-frame sound event logic, and claims <see cref="Sound.Player"/> as
    /// the active global listener. Call once per frame, only for the viewer currently active/visible -
    /// calling this from every open viewer's own renderer, active or not, would have each one stomp on
    /// whichever is the current global listener.
    /// </summary>
    public void Update(Camera camera)
    {
        Sound.Player = this;
        mixer.Update(camera.Location, camera.Forward);
        UpdateSoundscape(camera.Location);
    }

    /// <summary>
    /// A soundscape region (env_soundscape): while the listener is within <paramref name="Radius"/>
    /// of <paramref name="Position"/>, <paramref name="SoundEventName"/> plays as the ambient bed.
    /// </summary>
    public readonly record struct Soundscape(Vector3 Position, float Radius, string SoundEventName);

    private readonly List<Soundscape> soundscapes = [];
    private readonly HashSet<string> warmedSoundscapes = new(StringComparer.OrdinalIgnoreCase);
    private SoundEvent? activeSoundscape;
    private string? activeSoundscapeName;

    // How far outside a soundscape's radius its audio starts decoding. At run speed (250 u/s) this
    // buys the background decoder a few seconds before the soundscape can actually trigger.
    private const float SoundscapePrecacheMargin = 1024f;

    /// <summary>
    /// Registers a soundscape region. The closest in-range soundscape becomes the active ambient
    /// during <see cref="Update"/>; entering another region crossfades by stopping the previous event.
    /// </summary>
    public void AddSoundscape(Vector3 position, float radius, string soundEventName)
    {
        if (radius > 0f && !string.IsNullOrEmpty(soundEventName))
        {
            Cache(soundEventName);
            soundscapes.Add(new Soundscape(position, radius, soundEventName));
        }
    }

    private void UpdateSoundscape(Vector3 listenerPosition)
    {
        Soundscape? closest = null;
        var closestDistance = float.MaxValue;

        foreach (var soundscape in soundscapes)
        {
            var distance = Vector3.Distance(soundscape.Position, listenerPosition);

            if (distance < soundscape.Radius + SoundscapePrecacheMargin
                && warmedSoundscapes.Add(soundscape.SoundEventName))
            {
                // Approaching: re-queue the background decode in case the load-time precache got
                // evicted, so entering the radius (and every later child retrigger) plays warm.
                // Cached sounds make this a no-op that just refreshes their eviction age.
                Cache(soundscape.SoundEventName);
            }

            if (distance < soundscape.Radius && distance < closestDistance)
            {
                closest = soundscape;
                closestDistance = distance;
            }
        }

        if (closest == null)
        {
            // Left every soundscape radius, let the active one fade out
            activeSoundscape?.FadeOutAndStop();
            activeSoundscape = null;
            activeSoundscapeName = null;
            return;
        }

        // Compare by name (not handle) so a missing or unsupported event is not retried every frame.
        // A faded-out event (Started false) no longer counts as active, so re-entering the area restarts it.
        var currentStillActive = activeSoundscape is { Started: true } || (activeSoundscape == null && activeSoundscapeName != null);

        if (currentStillActive && string.Equals(activeSoundscapeName, closest.Value.SoundEventName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Crossfade: the outgoing ambient fades along its curve while the new one starts underneath
        activeSoundscape?.FadeOutAndStop();
        activeSoundscapeName = closest.Value.SoundEventName;

        // Soundscapes are the listener's ambient bed, play them unspatialized
        activeSoundscape = Play(closest.Value.SoundEventName);
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
