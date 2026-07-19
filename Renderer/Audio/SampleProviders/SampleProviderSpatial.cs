namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Applies per-ear volumes to a stereo stream based on the listener's orientation.
/// Volumes are interpolated across each buffer to avoid zipper noise.
/// </summary>
public abstract class SampleProviderSpatial : SampleProvider2D
{
    /// <summary>Gets the current left ear gain.</summary>
    public float LeftVolume { get; protected set; }
    /// <summary>Gets the current right ear gain.</summary>
    public float RightVolume { get; protected set; }

    /// <summary>Gets or sets the left ear gain used for the previous buffer, for interpolation.</summary>
    protected float LastLeftVolume { get; set; }
    /// <summary>Gets or sets the right ear gain used for the previous buffer, for interpolation.</summary>
    protected float LastRightVolume { get; set; }

    /// <summary>Creates a spatializer around the given source.</summary>
    protected SampleProviderSpatial(IAudioSampleProvider provider) : base(provider)
    {
    }

    /// <inheritdoc/>
    public override int Read(float[] buffer, int offset, int count)
    {
        var read = Provider.Read(buffer, offset, count);

        for (var i = 0; i < read; i++)
        {
            var left = i % 2 == 0;
            var lastVolume = left ? LastLeftVolume : LastRightVolume;
            var volume = left ? LeftVolume : RightVolume;
            buffer[offset + i] = float.Lerp(buffer[offset + i] * lastVolume, buffer[offset + i] * volume, (float)i / count);
        }

        LastLeftVolume = LeftVolume;
        LastRightVolume = RightVolume;

        if (read < count)
        {
            Over();
        }

        return read;
    }

    /// <summary>
    /// Recomputes the per-ear volumes for the given listener. Returns whether the sound is currently audible.
    /// </summary>
    public virtual bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        var dot = GetDirectionMix(listenerPosition, rightEarDirection);

        LeftVolume = Math.Max(-dot + 1, 0) * Volume;
        RightVolume = Math.Max(dot + 1, 0) * Volume;
        return true;
    }

    /// <summary>
    /// Returns how much the sound leans towards the right ear (-1 fully left, 1 fully right).
    /// </summary>
    protected abstract float GetDirectionMix(Vector3 listenerPosition, Vector3 rightEarDirection);
}
