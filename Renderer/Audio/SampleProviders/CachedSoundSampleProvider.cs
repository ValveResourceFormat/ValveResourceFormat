namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Streams samples out of a <see cref="CachedSound"/>, honoring its loop points.
/// </summary>
public sealed class CachedSoundSampleProvider : AudioSampleProvider
{
    private readonly CachedSound sound;
    private int position;

    public CachedSoundSampleProvider(CachedSound sound)
    {
        this.sound = sound;
    }

    /// <inheritdoc/>
    public override int Read(float[] buffer, int offset, int count)
    {
        var samples = sound.Samples;
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

        if (read < count)
        {
            Over();
        }

        return read;
    }
}
