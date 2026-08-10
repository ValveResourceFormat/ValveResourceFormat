namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Streams samples out of a <see cref="CachedSound"/>, honoring its loop points.
/// Supports pitch shifting by resampling at a fractional playback rate.
/// </summary>
public sealed class CachedSoundSampleProvider : AudioSampleProvider
{
    private const int ChannelCount = 2; // The mixer format is always stereo
    private const float PcmScale = 1f / 32768f;

    private CachedSound sound;
    private double framePosition;

    /// <summary>
    /// Gets or sets the playback rate multiplier: 1 is normal speed, higher is faster and higher pitched.
    /// </summary>
    public float Pitch { get; set; } = 1f;

    /// <summary>
    /// Gets or sets the doppler shift multiplying <see cref="Pitch"/>, written by the spatializer every
    /// update (see <see cref="SampleProvider3D.DopplerScale"/>) and kept separate from it so it does not
    /// have to know what pitch the sound event authored.
    /// </summary>
    public float DopplerShift { get; set; } = 1f;

    /// <summary>Gets the rate samples are actually consumed at.</summary>
    private float PlaybackRate => Pitch * DopplerShift;

    private int delaySamples;

    /// <summary>
    /// Gets or sets the delay before the sound starts, as an interleaved sample count. A negative value
    /// instead seeks that many samples ahead into the track (wrapping within its loop region, or its full
    /// length when it does not loop) - used to phase-stagger otherwise-identical looping tracks (e.g.
    /// quad-channel ambient beds) so they do not all loop in lockstep.
    /// </summary>
    public int DelaySamples
    {
        get => delaySamples;
        set
        {
            if (value >= 0)
            {
                delaySamples = value;
                return;
            }

            delaySamples = 0;

            var totalFrames = sound.SampleLength / ChannelCount;
            if (totalFrames <= 0)
            {
                return;
            }

            var loopStartFrame = sound.LoopStart >= 0 ? sound.LoopStart / ChannelCount : 0;
            var loopEndFrame = sound.LoopStart >= 0 ? Math.Min(sound.LoopEnd / ChannelCount, totalFrames) : totalFrames;
            var loopLength = Math.Max(loopEndFrame - loopStartFrame, 1);

            framePosition = loopStartFrame + ((-value / ChannelCount) % loopLength);
        }
    }

    /// <summary>
    /// Creates a provider that streams the given cached sound from the beginning.
    /// </summary>
    public CachedSoundSampleProvider(CachedSound sound)
    {
        this.sound = sound;
    }

    /// <summary>
    /// Rebinds this provider to <paramref name="newSound"/> and restarts playback from the beginning.
    /// Lets a retriggering sound event reuse the same provider instead of allocating a new one every time.
    /// </summary>
    public void Reset(CachedSound newSound)
    {
        sound = newSound;
        framePosition = 0;

        // The next play may not be spatialized at all, and nothing would clear a shift left behind by
        // whatever was moving during the last one
        DopplerShift = 1f;
    }

    /// <inheritdoc/>
    public override int Read(float[] buffer, int offset, int count)
    {
        // Mark the sound as in use so the cache does not evict it while it is still playing
        sound.LastUsed = System.Diagnostics.Stopwatch.GetTimestamp();

        if (!sound.Ready)
        {
            // Still decoding on the background thread: hold the line with silence until the samples arrive
            Array.Clear(buffer, offset, count);
            return count;
        }

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

        var read = PlaybackRate == 1f
            ? ReadDirect(buffer, offset + written, count - written)
            : ReadResampled(buffer, offset + written, count - written);

        return written + read;
    }

    private int ReadDirect(float[] buffer, int offset, int count)
    {
        var samples = sound.Samples;
        var sampleOffset = sound.SampleOffset;
        var sampleLength = sound.SampleLength;
        var position = (int)framePosition * ChannelCount;
        var read = 0;

        while (read < count)
        {
            var end = sound.LoopStart >= 0 ? Math.Min(sound.LoopEnd, sampleLength) : sampleLength;
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
            var dst = offset + read;
            for (var i = 0; i < toCopy; i++)
            {
                buffer[dst + i] = samples[sampleOffset + position + i] * PcmScale;
            }
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
        var rate = PlaybackRate;
        var samples = sound.Samples;
        var sampleOffset = sound.SampleOffset;
        var totalFrames = sound.SampleLength / ChannelCount;
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
                var s0 = samples[sampleOffset + frame0 * ChannelCount + ch] * PcmScale;
                var s1 = samples[sampleOffset + frame1 * ChannelCount + ch] * PcmScale;
                buffer[offset + read++] = float.Lerp(s0, s1, t);
            }

            framePosition += rate;
        }

        if (read < count)
        {
            Over();
        }

        return read;
    }
}
