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

    /// <summary>Gets or sets the left ear gain used for the previous buffer.</summary>
    protected float LastLeftVolume { get; set; }
    /// <summary>Gets or sets the right ear gain used for the previous buffer.</summary>
    protected float LastRightVolume { get; set; }

    /// <summary>Creates a spatializer around the given source.</summary>
    protected SampleProviderSpatial(IAudioSampleProvider provider) : base(provider)
    {
    }

    /// <inheritdoc/>
    public override int Read(float[] buffer, int offset, int count)
    {
        var read = Provider.Read(buffer, offset, count);

        // Interpolate across the samples actually returned, so the final written sample reaches the
        // target volume even when the provider ends mid-buffer
        var lastIndex = Math.Max(read - 1, 1);

        for (var i = 0; i < read; i++)
        {
            var left = i % 2 == 0;
            var lastVolume = left ? LastLeftVolume : LastRightVolume;
            var volume = left ? LeftVolume : RightVolume;
            buffer[offset + i] = float.Lerp(buffer[offset + i] * lastVolume, buffer[offset + i] * volume, (float)i / lastIndex);
        }

        // Where the sound is above, below or behind the listener, which no arrangement of the two gains
        // above can express. Skipped outright for the common case of a sound at ear level in front of them.
        if (cueSampleRate > 0 && cueFilters.Update(cueSampleRate, read / 2))
        {
            cueFilters.Process(buffer, offset, read);
        }

        LastLeftVolume = LeftVolume;
        LastRightVolume = RightVolume;

        if (read < count)
        {
            Over();
        }

        return read;
    }

    private bool volumesInitialized;

    /// <summary>
    /// How much of a sound behind the listener survives the fold into the front pair, for when the
    /// spectral cues are turned off. Stereo output has no rear speakers to pan to, so the engine mixes
    /// what would go to them in at three quarters. A broadband trim is a blunt stand-in for what the ear
    /// actually does to a sound from behind - see <see cref="SpectralCueFilters"/> - but it is what the
    /// engine's own stereo path does.
    /// </summary>
    private const float RearVolumeScale = 0.75f;

    private readonly SpectralCueFilters cueFilters = new();

    // Set alongside the filter targets; 0 until the first update, so Read leaves untouched anything that
    // has not been spatialized yet
    private int cueSampleRate;

    /// <summary>
    /// Recomputes the per-ear volumes for the given listener, through <see cref="ApplyPanning"/>.
    /// Returns whether the sound is currently audible.
    /// </summary>
    public abstract bool Update(in ListenerState listener);

    /// <summary>
    /// Splits <paramref name="gain"/> across the two ears by where <paramref name="listenerToSound"/>
    /// points, and folds the rear into the front pair.
    /// </summary>
    /// <param name="listenerToSound">Unit vector from the listener towards the sound.</param>
    /// <param name="listener">The listener to pan against.</param>
    /// <param name="gain">The gain to split, before panning.</param>
    private protected void ApplyPanning(Vector3 listenerToSound, in ListenerState listener, float gain)
    {
        var rightDot = Math.Clamp(Vector3.Dot(listenerToSound, listener.Right), -1f, 1f);
        var forwardDot = Math.Clamp(Vector3.Dot(listenerToSound, listener.Forward), -1f, 1f);

        if (listener.SpectralCueStrength > 0f)
        {
            // Elevation and front/back are both spectral, so they are voiced by the filters instead - see
            // SpectralCueFilters on why a rear sound is not simply a quieter one. Measured against the
            // listener's own up, so it follows their pitch rather than the world's.
            var upDot = Math.Clamp(Vector3.Dot(listenerToSound, listener.Up), -1f, 1f);

            cueFilters.SetTarget(
                float.RadiansToDegrees(MathF.Asin(upDot)),
                Math.Max(0f, -forwardDot),
                listener.SpectralCueStrength);

            cueSampleRate = listener.SampleRate;
        }
        else
        {
            cueFilters.SetTarget(0f, 0f, 0f);
            gain *= float.Lerp(1f, RearVolumeScale, (1f - forwardDot) * 0.5f);
        }

        // How much of the sound each ear is pointed at, (1 -/+ cos) / 2. The ratio between the two is
        // what localizes the sound, and the exponent sharpens or widens it without touching the level.
        var left = (1f - rightDot) * 0.5f;
        var right = (1f + rightDot) * 0.5f;

        if (listener.PanSharpness != 1f)
        {
            left = MathF.Pow(left, listener.PanSharpness);
            right = MathF.Pow(right, listener.PanSharpness);
        }

        var scale = gain / MathF.Sqrt((left * left) + (right * right));

        LeftVolume = left * scale;
        RightVolume = right * scale;
    }

    /// <summary>
    /// Snaps the interpolation state to the current volumes on the first update.
    /// </summary>
    protected void SnapVolumesOnFirstUpdate()
    {
        if (!volumesInitialized)
        {
            volumesInitialized = true;
            LastLeftVolume = LeftVolume;
            LastRightVolume = RightVolume;
        }
    }

    /// <summary>
    /// Resets the interpolation state so the next update snaps to its target instead of fading in from
    /// wherever this provider last left off. Call when reusing this instance for a new (e.g. retriggered) sound.
    /// </summary>
    public virtual void ResetInterpolation()
    {
        volumesInitialized = false;
        LastLeftVolume = 0f;
        LastRightVolume = 0f;
        cueFilters.Reset();
    }
}
