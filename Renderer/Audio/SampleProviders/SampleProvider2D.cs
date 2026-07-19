namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Plays an inner sample provider without spatialization, applying only <see cref="AudioSampleProvider.Volume"/>.
/// </summary>
public class SampleProvider2D : AudioSampleProvider
{
    protected IAudioSampleProvider Provider { get; }

    public SampleProvider2D(IAudioSampleProvider provider)
    {
        Provider = provider;
    }

    /// <inheritdoc/>
    public override int Read(float[] buffer, int offset, int count)
    {
        var read = Provider.Read(buffer, offset, count);

        if (VolumeMultiplier != 1f)
        {
            for (var i = 0; i < read; i++)
            {
                buffer[offset + i] *= VolumeMultiplier;
            }
        }

        if (read < count)
        {
            Over();
        }

        return read;
    }
}
