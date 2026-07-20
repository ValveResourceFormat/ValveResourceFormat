namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// A positional sound with stereo panning and distance based attenuation.
/// Attenuation follows <see cref="DistanceVolumeCurve"/> when set, otherwise a simple exponential falloff over <see cref="Range"/>.
/// </summary>
public class SampleProvider3D : SampleProviderSpatial
{
    /// <summary>Gets or sets the audible range used by the fallback falloff when no volume curve is set.</summary>
    public float Range { get; set; } = 512;

    /// <summary>Gets or sets the world position of the sound.</summary>
    public Vector3 Position { get; set; }

    /// <summary>Gets or sets the distance to volume mapping curve ("distance_volume_mapping_curve").</summary>
    public SoundEventCurve? DistanceVolumeCurve { get; set; }

    /// <summary>
    /// Gets or sets the distance to unfiltered-stereo mapping curve ("distance_unfiltered_stereo_mapping_curve").
    /// At 1 the sound plays as plain centered stereo (e.g. the listener's own footsteps), at 0 it is fully spatialized.
    /// </summary>
    public SoundEventCurve? StereoMixCurve { get; set; }

    /// <summary>Gets whether the listener was outside the audible range during the last update.</summary>
    public bool OutOfRange { get; private set; }

    /// <summary>
    /// Gets or sets the attenuation to move towards while geometry blocks the sound
    /// (1 unoccluded, 0 fully muted). The applied value is smoothed across updates so
    /// walking past a corner does not gate the sound on and off.
    /// </summary>
    public float OcclusionTarget { get; set; } = 1f;

    private float occlusion = -1f; // < 0 means not initialized, the first update snaps to the target

    /// <summary>Creates a positional provider around the given source.</summary>
    public SampleProvider3D(IAudioSampleProvider provider) : base(provider)
    {
    }

    /// <inheritdoc/>
    public override bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        var distance = (listenerPosition - Position).Length();

        occlusion = occlusion < 0f ? OcclusionTarget : float.Lerp(occlusion, OcclusionTarget, 0.1f);

        float attenuation;

        if (DistanceVolumeCurve != null)
        {
            attenuation = Math.Max(DistanceVolumeCurve.Evaluate(distance), 0f);
        }
        else if (distance <= Range)
        {
            var multiplier = 1f - distance / Range;
            attenuation = (float)((Math.Exp(multiplier) - 1) / (Math.E - 1));
        }
        else
        {
            attenuation = 0f;
        }

        attenuation *= occlusion;

        OutOfRange = attenuation <= 0f;
        if (OutOfRange)
        {
            LeftVolume = 0;
            RightVolume = 0;
            SnapVolumesOnFirstUpdate();
            return false;
        }

        base.Update(listenerPosition, rightEarDirection);

        LeftVolume *= attenuation;
        RightVolume *= attenuation;

        // Within the unfiltered-stereo distance the sound plays centered instead of panned
        var stereoMix = StereoMixCurve?.Evaluate(distance) ?? 0f;
        if (stereoMix > 0f)
        {
            var centerVolume = Volume * attenuation;
            LeftVolume = float.Lerp(LeftVolume, centerVolume, stereoMix);
            RightVolume = float.Lerp(RightVolume, centerVolume, stereoMix);
        }

        SnapVolumesOnFirstUpdate();
        return true;
    }

    /// <inheritdoc/>
    protected override float GetDirectionMix(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        var soundDirectionToListener = listenerPosition - Position;
        var distance = soundDirectionToListener.Length();

        if (distance <= float.Epsilon)
        {
            return 0f;
        }

        soundDirectionToListener /= distance;

        return Vector3.Dot(soundDirectionToListener, rightEarDirection);
    }
}
