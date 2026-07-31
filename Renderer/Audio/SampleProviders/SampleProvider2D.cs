namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Plays an inner sample provider without spatialization, applying only <see cref="AudioSampleProvider.Volume"/>.
/// </summary>
public class SampleProvider2D : AudioSampleProvider
{
    /// <summary>Gets the wrapped source provider.</summary>
    protected IAudioSampleProvider Provider { get; }

    /// <summary>Creates a 2D provider around the given source.</summary>
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
