using System.Runtime.InteropServices;
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
    private readonly Dictionary<string, Entry> sounds = [];
    private long cachedBytes;
    private long useCounter;

    private struct Entry
    {
        public CachedSound? Sound;
        public long LastUsed;
    }

    /// <summary>
    /// Gets or sets the budget for decoded audio in bytes. Decoding is far too expensive to redo per play
    /// (a second of stereo 48 kHz float is nearly 400 KB and every play would hitch the game thread), so
    /// sounds are kept decoded, and the least recently used ones are dropped once the budget is exceeded.
    /// Evicting only drops the cache's reference: a sound still playing holds its own and stays valid.
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
            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(sounds, fileName, out var exists);

            if (!exists)
            {
                entry.Sound = LoadSound(fileName);

                if (entry.Sound != null)
                {
                    cachedBytes += (long)entry.Sound.Samples.Length * sizeof(float);
                }
            }

            entry.LastUsed = ++useCounter;

            var sound = entry.Sound;

            if (cachedBytes > MaxCachedBytes)
            {
                // entry is a ref into the dictionary, do not touch it past this point
                Prune();
            }

            return sound;
        }
    }

    /// <summary>
    /// Drops least recently used sounds until the cache is back under budget. Failed loads are kept:
    /// they cost nothing and re-attempting them would mean hitting the disk again.
    /// </summary>
    private void Prune()
    {
        while (cachedBytes > MaxCachedBytes)
        {
            string? oldestKey = null;
            var oldestUse = long.MaxValue;

            foreach (var (key, entry) in sounds)
            {
                if (entry.Sound != null && entry.LastUsed < oldestUse)
                {
                    oldestUse = entry.LastUsed;
                    oldestKey = key;
                }
            }

            if (oldestKey == null)
            {
                break;
            }

            var evicted = sounds[oldestKey];
            sounds.Remove(oldestKey);
            cachedBytes -= (long)evicted.Sound!.Samples.Length * sizeof(float);

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

        var samples = AudioConverter.Convert(decoded.Samples, decoded.Channels, decoded.SampleRate, channels, sampleRate);

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
