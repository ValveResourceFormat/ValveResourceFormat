namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Streams samples out of a <see cref="CachedSound"/>, honoring its loop points.
/// Supports pitch shifting by resampling at a fractional playback rate.
/// </summary>
public sealed class CachedSoundSampleProvider : AudioSampleProvider
{
    private const int ChannelCount = 2; // The mixer format is always stereo

    private readonly CachedSound sound;
    private double framePosition;

    /// <summary>
    /// Gets or sets the playback rate multiplier: 1 is normal speed, higher is faster and higher pitched.
    /// </summary>
    public float Pitch { get; set; } = 1f;

    private int delaySamples;

    /// <summary>
    /// Gets or sets the delay before the sound starts, as an interleaved sample count
    /// ("delay" in the sound event data, e.g. the knife wall hit plays 90ms into the swing).
    /// </summary>
    public int DelaySamples { get => delaySamples; set => delaySamples = value; }

    /// <summary>
    /// Creates a provider that streams the given cached sound from the beginning.
    /// </summary>
    public CachedSoundSampleProvider(CachedSound sound)
    {
        this.sound = sound;
    }

    /// <inheritdoc/>
    public override int Read(float[] buffer, int offset, int count)
    {
        var written = 0;

        if (delaySamples > 0)
        {
            written = Math.Min(delaySamples, count);
            Array.Clear(buffer, offset, written);
            delaySamples -= written;

            if (written == count)
            {
                return count;
            }
        }

        var read = Pitch == 1f
            ? ReadDirect(buffer, offset + written, count - written)
            : ReadResampled(buffer, offset + written, count - written);

        return written + read;
    }

    private int ReadDirect(float[] buffer, int offset, int count)
    {
        var samples = sound.Samples;
        var position = (int)framePosition * ChannelCount;
        var read = 0;

        while (read < count)
        {
            var end = sound.LoopStart >= 0 ? Math.Min(sound.LoopEnd, samples.Length) : samples.Length;
            var available = end - position;

            if (available <= 0)
            {
                if (sound.LoopStart >= 0 && sound.LoopStart < end)
                {
                    position = sound.LoopStart;
                    continue;
                }

                break;
            }

            var toCopy = Math.Min(available, count - read);
            Array.Copy(samples, position, buffer, offset + read, toCopy);
            position += toCopy;
            read += toCopy;
        }

        framePosition = (double)position / ChannelCount;

        if (read < count)
        {
            Over();
        }

        return read;
    }

    private int ReadResampled(float[] buffer, int offset, int count)
    {
        var samples = sound.Samples;
        var totalFrames = samples.Length / ChannelCount;
        var loops = sound.LoopStart >= 0;
        var loopStartFrame = loops ? sound.LoopStart / ChannelCount : 0;
        var endFrame = loops ? Math.Min(sound.LoopEnd / ChannelCount, totalFrames) : totalFrames;

        var frames = count / ChannelCount;
        var read = 0;

        for (var i = 0; i < frames; i++)
        {
            if (framePosition >= endFrame)
            {
                if (loops && loopStartFrame < endFrame)
                {
                    framePosition = loopStartFrame + (framePosition - endFrame);
                }
                else
                {
                    break;
                }
            }

            var frame0 = (int)framePosition;
            var frame1 = Math.Min(frame0 + 1, endFrame - 1);
            var t = (float)(framePosition - frame0);

            for (var ch = 0; ch < ChannelCount; ch++)
            {
                var s0 = samples[frame0 * ChannelCount + ch];
                var s1 = samples[frame1 * ChannelCount + ch];
                buffer[offset + read++] = float.Lerp(s0, s1, t);
            }

            framePosition += Pitch;
        }

        if (read < count)
        {
            Over();
        }

        return read;
    }
}
