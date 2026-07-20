using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
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

    /// <summary>
    /// Gets or sets whether output is suspended. When set the mix fades to silence over a short ramp and
    /// stays silent until resumed, then fades back in - so losing and regaining focus does not snap the audio.
    /// The fade runs on the mixing thread, which keeps going while the render loop is paused.
    /// Sounds keep advancing while suspended; this only gates whether they are audible.
    /// </summary>
    public bool Suspended
    {
        get => suspended;
        set => suspended = value;
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
            volumeMultiplier = (float)((Math.Exp(value) - 1) / (Math.E - 1));
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

    /// <summary>
    /// Creates a sound event player. Takes ownership of <paramref name="device"/> and starts the mixing thread immediately.
    /// </summary>
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

        while (!stopping)
        {
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
                // Interpolate the gain across the chunk so a fade (or master volume change) does not step
                var frames = buffer.Length / channelCount;
                var index = 0;

                for (var frame = 0; frame < frames; frame++)
                {
                    var mul = float.Lerp(startMul, endMul, (float)frame / frames);

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
        foreach (var soundEventsFile in GetSoundEventFiles())
        {
            if (filter.Length == 0 || soundEventsFile.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                LoadSoundEventsFile(soundEventsFile);
            }
        }

        logger.LogInformation("Loaded {Count} sound events", Bank.Count);
    }

    /// <summary>
    /// Loads sound event definitions from a single soundevent (vsndevts) file.
    /// </summary>
    public void LoadSoundEventsFile(string fileName)
    {
        using var soundEventsFile = fileLoader.LoadFileCompiled(fileName);
        if (soundEventsFile?.DataBlock == null)
        {
            logger.LogWarning("Could not load sound events file {FileName}", fileName);
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
    /// <param name="volume">Optional programmatic volume, replacing the definition's volume property (some events, e.g. gear rustles, expect this).</param>
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
    /// Decodes and caches every vsnd a sound event could play - all random track variants and any child events -
    /// so the first real <see cref="Play"/> does not hitch the game thread on decode. Meant to be called ahead of
    /// time (e.g. when a map loads) for the sound events it uses; it blocks while decoding, which is fine off the
    /// hot path. Unknown events are ignored. More thorough than a silent play, which would decode only one of the
    /// random track variants.
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

        foreach (var track in definition.TrackNames)
        {
            SoundCache.GetSound(track);
        }

        foreach (var childName in definition.ChildEventNames)
        {
            var child = Bank.GetSoundEvent(childName);
            if (child != null)
            {
                Cache(child, depth + 1);
            }
        }
    }

    /// <summary>
    /// Implements "block_matching_events": the same event cannot be played again within its "block_duration".
    /// </summary>
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

    /// <summary>
    /// Picks a random track index, never repeating the previously picked track for the same sound event definition.
    /// </summary>
    internal int PickTrack(SoundEventDefinition definition)
    {
        var trackCount = definition.TrackNames.Length;

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
    /// Stops the sound currently playing on the given channel, if any.
    /// </summary>
    public void StopChannel(string channel)
    {
        if (channels.Remove(channel, out var soundEvent))
        {
            soundEvent.Stop();
        }
    }

    /// <summary>
    /// TEMP debug: collects the position and vsnd name of every audible positioned sound.
    /// </summary>
    public void CollectDebugSounds(List<(Vector3 Position, string Text)> results) => mixer.CollectDebugSounds(results);

    /// <summary>
    /// Updates listener position and per-frame sound event logic. Call once per frame from the render/game thread.
    /// </summary>
    public void Update(Camera camera)
    {
        mixer.Update(camera.Location, camera.Forward);
    }

    private IEnumerable<string> GetSoundEventFiles()
    {
        using var manifestResource = fileLoader.LoadFileCompiled("soundevents/soundevents_manifest.vrman");

        if (manifestResource?.DataBlock is not ResourceManifest manifest)
        {
            yield break;
        }

        foreach (var resourceGroup in manifest.Resources)
        {
            foreach (var soundEventsFile in resourceGroup)
            {
                yield return soundEventsFile;
            }
        }
    }

    /// <summary>
    /// Stops the mixing thread and disposes the mixer and the audio device.
    /// </summary>
    public void Dispose()
    {
        if (Sound.Player == this)
        {
            Sound.Player = null;
        }

        stopping = true;

        if (!mixingThread.Join(TimeSpan.FromSeconds(1)))
        {
            logger.LogWarning("Sound mixing thread did not stop in time");
        }

        mixer.Dispose();
        device.Dispose();
    }
}
