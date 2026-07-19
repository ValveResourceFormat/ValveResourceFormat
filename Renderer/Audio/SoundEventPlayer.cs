using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Plays sound events through an injected <see cref="IAudioDevice"/>.
/// Owns a mixing thread that continuously mixes active sounds and submits the samples to the device.
/// </summary>
public sealed class SoundEventPlayer : IDisposable
{
    private const int MixChunkFrames = 512;

    /// <summary>Gets the decoded sound cache.</summary>
    public SoundCache Cache { get; }

    /// <summary>Gets the bank of loaded sound event definitions.</summary>
    public SoundEventBank Bank { get; }

    /// <summary>Gets the mixer output sample rate, taken from the device.</summary>
    public int SampleRate => device.SampleRate;

    private readonly IFileLoader fileLoader;
    private readonly IAudioDevice device;
    private readonly ILogger logger;
    private readonly AudioMixer mixer;
    private readonly Thread mixingThread;
    private readonly Dictionary<string, SoundEvent> channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> blockTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<KVObject, int> lastTrackIndices = new(ReferenceEqualityComparer.Instance);
    private volatile bool stopping;

    /// <summary>
    /// Creates a sound event player. Takes ownership of <paramref name="device"/> and starts the mixing thread immediately.
    /// </summary>
    public SoundEventPlayer(IFileLoader fileLoader, IAudioDevice device, ILogger? logger = null)
    {
        this.fileLoader = fileLoader;
        this.device = device;
        this.logger = logger ?? NullLogger.Instance;

        Cache = new SoundCache(fileLoader, device.SampleRate, device.Channels, this.logger);
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
        var buffer = new float[MixChunkFrames * device.Channels];

        while (!stopping)
        {
            mixer.Read(buffer, 0, buffer.Length);
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
        var soundEventData = Bank.GetSoundEvent(soundEventName);
        if (soundEventData == null)
        {
            logger.LogWarning("Unknown sound event {SoundEventName}", soundEventName);
            return null;
        }

        if (IsBlocked(soundEventName, soundEventData))
        {
            return null;
        }

        var soundEvent = SoundEvent.Build(soundEventData);
        if (soundEvent == null)
        {
            logger.LogWarning("Unsupported sound event type {Type} for {SoundEventName}",
                soundEventData.GetStringProperty("type", string.Empty), soundEventName);
            return null;
        }

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
    /// Implements "block_matching_events": the same event cannot be played again within its "block_duration".
    /// </summary>
    private bool IsBlocked(string soundEventName, KVObject soundEventData)
    {
        if (!soundEventData.GetBooleanProperty("block_matching_events"))
        {
            return false;
        }

        var blockDuration = soundEventData.GetFloatProperty("block_duration");
        if (blockDuration <= 0f)
        {
            return false;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();

        if (blockTimestamps.TryGetValue(soundEventName, out var lastPlayed)
            && (now - lastPlayed) / (double)System.Diagnostics.Stopwatch.Frequency < blockDuration)
        {
            return true;
        }

        blockTimestamps[soundEventName] = now;
        return false;
    }

    /// <summary>
    /// Picks a random track index using the playing event's random source,
    /// never repeating the previously picked track for the same sound event definition.
    /// </summary>
    internal int PickTrack(KVObject soundEventData, int trackCount, Random random)
    {
        if (trackCount <= 1)
        {
            return 0;
        }

        int index;

        if (lastTrackIndices.TryGetValue(soundEventData, out var last))
        {
            // Pick from the remaining tracks and skip over the last one
            index = random.Next(trackCount - 1);
            if (index >= last)
            {
                index++;
            }
        }
        else
        {
            index = random.Next(trackCount);
        }

        lastTrackIndices[soundEventData] = index;
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
