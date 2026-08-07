namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// The root mixer. Unlike <see cref="SampleProviderMulti"/> it always produces a full buffer,
/// padding with silence.
/// </summary>
public sealed class SampleProviderMixer : SampleProviderMulti
{
    /// <inheritdoc/>
    public override int Read(float[] buffer, int offset, int count)
    {
        var read = base.Read(buffer, offset, count);

        if (read < count)
        {
            Array.Clear(buffer, offset + read, count - read);
        }

        return count;
    }
}
