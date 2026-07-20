using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer.Audio.Decoders;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Loads compiled sound resources (vsnd) and caches them fully decoded in the mixer's output format.
/// Decoding happens on a background thread: <see cref="GetSound"/> returns a placeholder immediately and
/// providers play silence until it is ready, so neither the game thread nor the mixing thread ever block on decode.
/// </summary>
public sealed class SoundCache : IDisposable
{
    private readonly record struct DecodeRequest(string FileName, CachedSound Sound);

    private readonly IFileLoader fileLoader;
    private readonly ILogger logger;
    private readonly int sampleRate;
    private readonly int channels;
    private readonly Dictionary<string, CachedSound> sounds = [];
    private readonly LinkedList<DecodeRequest> decodeQueue = [];
    private readonly Thread decodeThread;
    private volatile bool stopping;
    private long cachedBytes;

    // A sound read within this window is treated as still playing and is never evicted, so the budget is soft:
    // it floats above the limit while long sounds play and trims back down once they finish.
    private static readonly long GraceTicks = Stopwatch.Frequency; // 1 second

    // Sounds at or below this decoded size are never evicted. Small frequent one-shots (footsteps, gear,
    // impacts - a ~1s clip is ~190 KB) must never pay a re-decode delay just because large soundscape
    // ambients churned the cache; the big beds are always the ones pruned, their re-decode is inaudible.
    private const long SmallSoundProtectionBytes = 512 * 1024;

    /// <summary>
    /// Gets or sets the soft cache budget in bytes. Nothing is allocated up front - decoded sounds accumulate as
    /// they are played (a second of stereo 48 kHz PCM16 is ~190 KB). Once the total exceeds the budget the least
    /// recently used <em>idle</em> sounds are dropped; sounds that are currently playing are never evicted, so the
    /// total can float above the limit while several long sounds play. Evicting only drops the cache's reference:
    /// a sound still playing holds its own and stays valid, so an evicted sound simply re-decodes if played again.
    /// Small one-shots are never evicted (see <see cref="SmallSoundProtectionBytes"/>), so the total also floats
    /// when the small-clip working set alone exceeds the budget.
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

        decodeThread = new Thread(DecodeLoop)
        {
            IsBackground = true,
            Name = "VRF Sound Decoder",
        };
        decodeThread.Start();
    }

    /// <summary>
    /// Gets a sound by its file name (e.g. "sounds/player/footstep01.vsnd"). Returns immediately: when the sound
    /// is not decoded yet, the returned <see cref="CachedSound"/> is a placeholder that providers play as silence
    /// until the background decode finishes. Foreground requests (a sound that wants to play now) jump ahead of
    /// pending <paramref name="background"/> pre-cache requests in the decode queue.
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
                decodeQueue.AddLast(request);
            }
            else
            {
                decodeQueue.AddFirst(request);
            }

            Monitor.Pulse(sounds);
            return sound;
        }
    }

    /// <summary>
    /// Moves a pending decode to the front of the queue. Caller holds the lock.
    /// </summary>
    private void PrioritizeLocked(CachedSound sound)
    {
        for (var node = decodeQueue.First; node != null; node = node.Next)
        {
            if (ReferenceEquals(node.Value.Sound, sound))
            {
                decodeQueue.Remove(node);
                decodeQueue.AddFirst(node.Value);
                return;
            }
        }
    }

    private void DecodeLoop()
    {
        while (!stopping)
        {
            DecodeRequest request;

            lock (sounds)
            {
                while (decodeQueue.Count == 0)
                {
                    Monitor.Wait(sounds);

                    if (stopping)
                    {
                        return;
                    }
                }

                request = decodeQueue.First!.Value;
                decodeQueue.RemoveFirst();
            }

            try
            {
                DecodeSound(request.FileName, request.Sound);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to decode sound file {FileName}", request.FileName);
            }

            // Publish after the samples and loop points are assigned; the volatile write makes them visible
            request.Sound.Ready = true;

            lock (sounds)
            {
                cachedBytes += (long)request.Sound.Samples.Length * sizeof(short);

                if (cachedBytes > MaxCachedBytes)
                {
                    Prune();
                }
            }
        }
    }

    /// <summary>
    /// Drops least recently used <em>idle</em> sounds until the cache is back under budget, or stops early when
    /// everything left is still playing (the budget is soft). Failed loads are kept as a negative cache;
    /// a sound currently playing holds its own reference and stays valid even if evicted. Caller holds the lock.
    /// </summary>
    private void Prune()
    {
        var now = Stopwatch.GetTimestamp();

        // Bounded per pass: the lock is shared with GetSound on the game thread, so eviction work
        // must stay small. Catch-up continues after the next decode; briefly floating over budget is fine.
        var evictionsLeft = 4;

        while (cachedBytes > MaxCachedBytes && evictionsLeft-- > 0)
        {
            string? oldestKey = null;
            var oldest = long.MaxValue;

            foreach (var (key, sound) in sounds)
            {
                // Skip sounds that are not decoded yet or failed (zero bytes to reclaim), protected small
                // one-shots, and sounds read within the grace window: those are playing right now, so evicting
                // them would not free memory (the provider still holds the buffer) and would re-decode.
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

            logger.LogDebug("Evicted {FileName} from the sound cache", oldestKey);
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

        var floatSamples = AudioConverter.Convert(decoded.Samples, decoded.Channels, decoded.SampleRate, channels, sampleRate);
        var samples = AudioConverter.ToPcm16(floatSamples);

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
    /// Stops the background decode thread.
    /// </summary>
    public void Dispose()
    {
        stopping = true;

        lock (sounds)
        {
            Monitor.Pulse(sounds);
        }

        decodeThread.Join(TimeSpan.FromSeconds(2));
    }
}
