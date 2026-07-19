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
            }

            return sound;
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
