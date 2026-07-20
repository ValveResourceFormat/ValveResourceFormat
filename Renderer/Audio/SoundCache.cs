using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Audio.Decoders;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Loads compiled sound resources (vsnd) and caches them fully decoded in the mixer's output format.
/// </summary>
public sealed class SoundCache
{
    private readonly IFileLoader fileLoader;
    private readonly ILogger logger;
    private readonly int sampleRate;
    private readonly int channels;
    private readonly Dictionary<string, CachedSound?> sounds = [];
    private long cachedBytes;

    // A sound read within this window is treated as still playing and is never evicted, so the budget is soft:
    // it floats above the limit while long sounds play and trims back down once they finish.
    private static readonly long GraceTicks = Stopwatch.Frequency; // 1 second

    /// <summary>
    /// Gets or sets the soft cache budget in bytes. Nothing is allocated up front - decoded sounds accumulate as
    /// they are played (a second of stereo 48 kHz PCM16 is ~190 KB). Once the total exceeds the budget the least
    /// recently used <em>idle</em> sounds are dropped; sounds that are currently playing are never evicted, so the
    /// total can float above the limit while several long sounds play. Evicting only drops the cache's reference:
    /// a sound still playing holds its own and stays valid, so an evicted sound simply re-decodes if played again.
    /// </summary>
    public long MaxCachedBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Gets the total size of the decoded audio currently held.</summary>
    public long CachedBytes => Interlocked.Read(ref cachedBytes);

    /// <summary>Creates a sound cache that decodes into the given output format.</summary>
    public SoundCache(IFileLoader fileLoader, int sampleRate, int channels, ILogger logger)
    {
        this.fileLoader = fileLoader;
        this.sampleRate = sampleRate;
        this.channels = channels;
        this.logger = logger;
    }

    /// <summary>
    /// Gets a decoded sound by its file name (e.g. "sounds/player/footstep01.vsnd"), or null if it could not be loaded.
    /// </summary>
    public CachedSound? GetSound(string fileName)
    {
        lock (sounds)
        {
            if (!sounds.TryGetValue(fileName, out var sound))
            {
                sound = LoadSound(fileName);
                sounds.Add(fileName, sound);

                if (sound != null)
                {
                    cachedBytes += (long)sound.Samples.Length * sizeof(short);
                }
            }

            if (sound != null)
            {
                sound.LastUsed = Stopwatch.GetTimestamp();
            }

            if (cachedBytes > MaxCachedBytes)
            {
                Prune();
            }

            return sound;
        }
    }

    /// <summary>
    /// Drops least recently used <em>idle</em> sounds until the cache is back under budget, or stops early when
    /// everything left is still playing (the budget is soft). Failed loads (null) are kept as a negative cache;
    /// a sound currently playing holds its own reference and stays valid even if evicted.
    /// </summary>
    private void Prune()
    {
        var now = Stopwatch.GetTimestamp();

        while (cachedBytes > MaxCachedBytes)
        {
            string? oldestKey = null;
            var oldest = long.MaxValue;

            foreach (var (key, sound) in sounds)
            {
                // Skip failed loads, and skip sounds read within the grace window: they are playing right now, so
                // evicting them would not free memory (the provider still holds the buffer) and would re-decode.
                if (sound == null || now - sound.LastUsed < GraceTicks)
                {
                    continue;
                }

                if (sound.LastUsed < oldest)
                {
                    oldest = sound.LastUsed;
                    oldestKey = key;
                }
            }

            if (oldestKey == null)
            {
                // Everything over budget is still playing - let the total float above the limit until it stops
                break;
            }

            var evicted = sounds[oldestKey]!;
            sounds.Remove(oldestKey);
            cachedBytes -= (long)evicted.Samples.Length * sizeof(short);

            logger.LogDebug("Evicted {FileName} from the sound cache", oldestKey);
        }
    }

    private CachedSound? LoadSound(string fileName)
    {
        using var resource = fileLoader.LoadFileCompiled(fileName);

        if (resource?.DataBlock is not ResourceTypes.Sound soundData || soundData.StreamingDataSize == 0)
        {
            logger.LogWarning("Could not load sound file {FileName}", fileName);
            return null;
        }

        using var stream = soundData.GetSoundStream();

        DecodedAudio? decoded;

        switch (soundData.SoundType)
        {
            case ResourceTypes.Sound.AudioFileType.WAV:
                decoded = WavDecoder.Decode(stream);
                break;
            case ResourceTypes.Sound.AudioFileType.MP3:
                decoded = Mp3Decoder.Decode(stream);
                break;
            default:
                logger.LogWarning("Unsupported audio type {SoundType} for {FileName}", soundData.SoundType, fileName);
                return null;
        }

        if (decoded == null)
        {
            logger.LogWarning("Failed to decode sound file {FileName}", fileName);
            return null;
        }

        var floatSamples = AudioConverter.Convert(decoded.Samples, decoded.Channels, decoded.SampleRate, channels, sampleRate);
        var samples = AudioConverter.ToPcm16(floatSamples);

        var loopStart = -1;
        var loopEnd = samples.Length;

        if (soundData.LoopStart >= 0)
        {
            // Loop points are sample frame indices in the source sound, scale them to the mixer format
            loopStart = (int)((long)soundData.LoopStart * sampleRate / decoded.SampleRate) * channels;
            loopEnd = soundData.LoopEnd > 0
                ? (int)((long)soundData.LoopEnd * sampleRate / decoded.SampleRate) * channels
                : samples.Length;

            loopStart = Math.Clamp(loopStart, 0, samples.Length);
            loopEnd = Math.Clamp(loopEnd, loopStart, samples.Length);
        }

        return new CachedSound
        {
            Samples = samples,
            LoopStart = loopStart,
            LoopEnd = loopEnd,
        };
    }
}
