using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Audio.Decoders;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Loads compiled sound resources (vsnd) and caches them decoded in the mixer's output format.
/// <see cref="GetSound"/> returns a placeholder immediately; decoding happens on worker threads.
/// </summary>
public sealed class SoundCache : IDisposable
{
    private readonly record struct DecodeRequest(string FileName, CachedSound Sound);

    private readonly IFileLoader fileLoader;
    private readonly ILogger logger;
    private readonly int sampleRate;
    private readonly int channels;
    private readonly Dictionary<string, CachedSound> sounds = [];

    // Two decode lanes: a request to play now must never wait behind a bulk pre-cache decode
    // already in flight (a multi-minute ambient can take hundreds of ms to decode).
    private readonly LinkedList<DecodeRequest> foregroundQueue = [];
    private readonly LinkedList<DecodeRequest> backgroundQueue = [];
    private readonly Thread foregroundThread;
    private readonly Thread backgroundThread;
    private volatile bool stopping;
    private long cachedBytes;

    // A sound read within this window is treated as still playing and is never evicted; the budget is
    // soft and floats above the limit while long sounds play, trimming back down once they finish.
    private static readonly long GraceTicks = Stopwatch.Frequency; // 1 second

    // Sounds at or below this decoded size are never evicted: small frequent one-shots (footsteps, gear,
    // impacts) must never pay a re-decode delay just because large soundscape ambients churned the cache.
    private const long SmallSoundProtectionBytes = 512 * 1024;

    /// <summary>
    /// Gets or sets the soft cache budget in bytes. Least recently used idle sounds are evicted once the
    /// total exceeds this; sounds currently playing or below <see cref="SmallSoundProtectionBytes"/> are never evicted.
    /// </summary>
    public long MaxCachedBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>Gets the total size of the decoded audio currently held.</summary>
    public long CachedBytes => Interlocked.Read(ref cachedBytes);

    /// <summary>Creates a sound cache that decodes into the given output format.</summary>
    public SoundCache(IFileLoader fileLoader, int sampleRate, int channels, ILogger logger)
    {
        this.fileLoader = fileLoader;
        this.sampleRate = sampleRate;
        this.channels = channels;
        this.logger = logger;

        foregroundThread = new Thread(() => DecodeLoop(foregroundQueue))
        {
            IsBackground = true,
            Name = "VRF Sound Decoder",
        };
        backgroundThread = new Thread(() => DecodeLoop(backgroundQueue))
        {
            IsBackground = true,
            Name = "VRF Sound Precache",
            // Bulk pre-cache work (map load, soundscape warm-up) must not compete with the render thread
            Priority = ThreadPriority.BelowNormal,
        };
        foregroundThread.Start();
        backgroundThread.Start();
    }

    /// <summary>
    /// Gets a sound by file name, decoding it if necessary. Returns immediately with a placeholder if not yet
    /// decoded; foreground requests jump ahead of pending <paramref name="background"/> requests in the queue.
    /// </summary>
    public CachedSound GetSound(string fileName, bool background = false)
    {
        lock (sounds)
        {
            if (sounds.TryGetValue(fileName, out var sound))
            {
                sound.LastUsed = Stopwatch.GetTimestamp();

                if (!background && !sound.Ready)
                {
                    PrioritizeLocked(sound);
                }

                return sound;
            }

            sound = new CachedSound
            {
                LastUsed = Stopwatch.GetTimestamp(),
            };
            sounds.Add(fileName, sound);

            var request = new DecodeRequest(fileName, sound);

            if (background)
            {
                backgroundQueue.AddLast(request);
            }
            else
            {
                foregroundQueue.AddLast(request);
            }

            // Both lane threads wait on the same lock; wake both, only the matching one has work
            Monitor.PulseAll(sounds);
            return sound;
        }
    }

    /// <summary>
    /// Moves a decode that is still pending in the background lane to the front of the foreground lane.
    /// Caller holds the lock.
    /// </summary>
    private void PrioritizeLocked(CachedSound sound)
    {
        for (var node = backgroundQueue.First; node != null; node = node.Next)
        {
            if (ReferenceEquals(node.Value.Sound, sound))
            {
                backgroundQueue.Remove(node);
                foregroundQueue.AddFirst(node.Value);
                Monitor.PulseAll(sounds);
                return;
            }
        }
    }

    private void DecodeLoop(LinkedList<DecodeRequest> queue)
    {
        while (!stopping)
        {
            DecodeRequest request;

            lock (sounds)
            {
                while (queue.Count == 0)
                {
                    Monitor.Wait(sounds);

                    if (stopping)
                    {
                        return;
                    }
                }

                request = queue.First!.Value;
                queue.RemoveFirst();
            }

            try
            {
                DecodeSound(request.FileName, request.Sound);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to decode sound file {FileName}", request.FileName);
            }

            lock (sounds)
            {
                // Set Ready inside the lock so the other lane's Prune cannot evict this sound between
                // the publish and the byte accounting below - that race would subtract bytes never added.
                request.Sound.Ready = true;

                cachedBytes += (long)request.Sound.Samples.Length * sizeof(short);

                if (cachedBytes > MaxCachedBytes)
                {
                    Prune();
                }
            }
        }
    }

    /// <summary>
    /// Drops least recently used idle sounds until back under budget, or stops early if everything left
    /// is still playing. Failed decodes are kept as a negative cache. Caller holds the lock.
    /// </summary>
    private void Prune()
    {
        var now = Stopwatch.GetTimestamp();

        // Bounded per pass since the lock is shared with GetSound on the game thread; catch-up
        // continues after the next decode.
        var evictionsLeft = 4;

        while (cachedBytes > MaxCachedBytes && evictionsLeft-- > 0)
        {
            string? oldestKey = null;
            var oldest = long.MaxValue;

            foreach (var (key, sound) in sounds)
            {
                // Skip undecoded/failed sounds (zero bytes to reclaim), protected small one-shots, and
                // sounds read within the grace window (still playing - the provider still holds the buffer).
                if (!sound.Ready
                    || sound.Samples.Length == 0
                    || (long)sound.Samples.Length * sizeof(short) <= SmallSoundProtectionBytes
                    || now - sound.LastUsed < GraceTicks)
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

            var evicted = sounds[oldestKey];
            sounds.Remove(oldestKey);
            cachedBytes -= (long)evicted.Samples.Length * sizeof(short);

            logger.LogInformation("Evicted {FileName} from the sound cache", oldestKey);
        }
    }

    private void DecodeSound(string fileName, CachedSound sound)
    {
        using var resource = fileLoader.LoadFileCompiled(fileName);

        if (resource?.DataBlock is not ResourceTypes.Sound soundData || soundData.StreamingDataSize == 0)
        {
            logger.LogWarning("Could not load sound file {FileName}", fileName);
            return;
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
                return;
        }

        if (decoded == null)
        {
            logger.LogWarning("Failed to decode sound file {FileName}", fileName);
            return;
        }

        // Compare against the sample count from the resource: an incomplete decode (e.g. a decoder
        // silently giving up partway through the file) must not be trusted for looping.
        var decodedTruncated = decoded.Truncated;

        if (soundData.SampleCount > 0 && decoded.SampleRate > 0)
        {
            var expectedSeconds = (double)soundData.SampleCount / soundData.SampleRate;
            var decodedSeconds = (double)decoded.Samples.Length / decoded.Channels / decoded.SampleRate;

            if (decodedSeconds < expectedSeconds * 0.95)
            {
                logger.LogWarning(
                    "Sound {FileName} decoded incompletely: got {DecodedSeconds:F2}s of {ExpectedSeconds:F2}s ({SampleRate} Hz, {SoundType})",
                    fileName, decodedSeconds, expectedSeconds, soundData.SampleRate, soundData.SoundType);
                decodedTruncated = true;
            }
        }

        var samples = AudioConverter.ConvertToPcm16(decoded.Samples, decoded.Channels, decoded.SampleRate, channels, sampleRate);

        var loopStart = -1;
        var loopEnd = samples.Length;

        if (soundData.LoopStart >= 0 && decodedTruncated)
        {
            // The decode lost samples: looping the incomplete audio would
            // audibly repeat a fragment, play it through once instead
            logger.LogWarning("Sound {FileName} decoded incompletely, disabling its loop", fileName);
        }
        else if (soundData.LoopStart >= 0)
        {
            // Loop points are sample frame indices in the source sound, scale them to the mixer format
            loopStart = (int)((long)soundData.LoopStart * sampleRate / decoded.SampleRate) * channels;
            loopEnd = soundData.LoopEnd > 0
                ? (int)((long)soundData.LoopEnd * sampleRate / decoded.SampleRate) * channels
                : samples.Length;

            loopStart = Math.Clamp(loopStart, 0, samples.Length);
            loopEnd = Math.Clamp(loopEnd, loopStart, samples.Length);
        }

        sound.Samples = samples;
        sound.LoopStart = loopStart;
        sound.LoopEnd = loopEnd;
    }

    /// <summary>
    /// Stops the decode threads.
    /// </summary>
    public void Dispose()
    {
        stopping = true;

        lock (sounds)
        {
            Monitor.PulseAll(sounds);
        }

        foregroundThread.Join(TimeSpan.FromSeconds(2));
        backgroundThread.Join(TimeSpan.FromSeconds(2));
    }
}
