using ValveResourceFormat.Renderer.Utils;

namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// A positional sound with stereo panning and distance based attenuation.
/// Attenuation follows <see cref="DistanceVolumeCurve"/> when set, then <see cref="DistanceMult"/>,
/// otherwise a simple exponential falloff over <see cref="Range"/>.
/// </summary>
public class SampleProvider3D : SampleProviderSpatial
{
    /// <summary>Gets or sets the audible range used by the fallback falloff when no volume curve or distance multiplier is set.</summary>
    public float Range { get; set; } = 512;

    /// <summary>Gets or sets the world position of the sound.</summary>
    public Vector3 Position { get; set; }

    /// <summary>Gets or sets the distance to volume mapping curve ("distance_volume_mapping_curve").</summary>
    public SoundEventCurve? DistanceVolumeCurve { get; set; }

    /// <summary>
    /// Gets or sets the reciprocal of the distance this sound stays at full volume out to, past which it
    /// follows the inverse distance law (halving in amplitude per doubling of distance) instead of running
    /// out of range - the engine's own attenuation model, derived from a sound level (see
    /// <see cref="SoundscapeOperatorParsing.SoundLevelToDistanceMult"/>). Zero attenuates nothing at all
    /// (SNDLVL_NONE), null leaves attenuation to the curve or to <see cref="Range"/>.
    /// </summary>
    public float? DistanceMult { get; set; }

    /// <summary>
    /// Gets or sets the distance to unfiltered-stereo mapping curve ("distance_unfiltered_stereo_mapping_curve").
    /// At 1 the sound plays as plain centered stereo, at 0 it is fully spatialized.
    /// </summary>
    public SoundEventCurve? StereoMixCurve { get; set; }

    /// <summary>Gets whether the listener was outside the audible range during the last update.</summary>
    public bool OutOfRange { get; private set; }

    /// <summary>
    /// Gets or sets the attenuation to move towards while geometry blocks the sound
    /// (1 unoccluded, 0 fully muted). The applied value is smoothed across updates.
    /// </summary>
    public float OcclusionTarget { get; set; } = 1f;

    /// <summary>
    /// Gets or sets how strongly the doppler shift applies, 0 to disable it. The shift itself comes from
    /// how fast the listener and this sound close on each other; scaling past 1 exaggerates it.
    /// </summary>
    public float DopplerScale { get; set; } = 1f;

    // Time constant of the occlusion ramp. The engine smooths occlusion over a fixed quarter second,
    // so this cannot be a per-update lerp factor - that ramps at whatever rate the viewer happens to run at.
    private const float OcclusionSmoothingSeconds = 0.25f;

    // Shorter than the occlusion ramp: this only takes the jitter out of a position sampled once a frame.
    private const float VelocitySmoothingSeconds = 0.05f;

    // Speed of sound, in Valve units (inches) per second.
    private const float SpeedOfSound = 13503.9f;

    // A sound that moved further than this in a second was repositioned rather than moved (a retriggered
    // one shot picking a new spot, an emitter following an entity that teleported) - no doppler for it.
    private const float MaxSourceSpeed = 4000f;

    // Closer than this the listener is effectively on top of the sound and there is no approach axis left.
    private const float MinDopplerDistance = 8f;

    private float occlusion = -1f; // < 0 means not initialized, the first update snaps to the target

    // The track this provider pitches for doppler, when it wraps one directly
    private readonly CachedSoundSampleProvider? dopplerTarget;

    private Vector3 lastPosition;
    private bool hasLastPosition;
    private Vector3 velocity;

    /// <summary>Creates a positional provider around the given source.</summary>
    public SampleProvider3D(IAudioSampleProvider provider) : base(provider)
    {
        dopplerTarget = provider as CachedSoundSampleProvider;
    }

    /// <inheritdoc/>
    public override void ResetInterpolation()
    {
        base.ResetInterpolation();
        occlusion = -1f;

        // A reused provider is a new sound somewhere else entirely: the old position says nothing about
        // how fast this one is moving
        hasLastPosition = false;
        velocity = Vector3.Zero;
    }

    /// <inheritdoc/>
    public override bool Update(in ListenerState listener)
    {
        var listenerToSound = Position - listener.Position;
        var distance = listenerToSound.Length();
        var direction = distance > float.Epsilon ? listenerToSound / distance : listener.Forward;

        UpdateOcclusion(listener.DeltaTime);
        UpdateDoppler(listener, direction, distance);

        float attenuation;

        if (DistanceVolumeCurve != null)
        {
            attenuation = Math.Max(DistanceVolumeCurve.Evaluate(distance), 0f);
        }
        else if (DistanceMult is { } distanceMult)
        {
            // Full volume within the sound's reference distance, 1/d past it. Unlike a range this never
            // reaches zero, so it is cut off where it stops being audible instead
            var relativeDistance = distance * distanceMult;
            attenuation = relativeDistance > 1f ? 1f / relativeDistance : 1f;

            if (attenuation < InaudibleGain)
            {
                attenuation = 0f;
            }
        }
        else if (distance <= Range)
        {
            var multiplier = 1f - distance / Range;
            attenuation = MathUtils.ToPerceptualVolume(multiplier);
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

        ApplyPanning(direction, listener, Volume * attenuation);

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

    // Where the inverse distance law is quiet enough to stop mixing the sound: -48 dB.
    private const float InaudibleGain = 1f / 256f;

    private void UpdateOcclusion(float deltaTime)
    {
        if (occlusion < 0f)
        {
            occlusion = OcclusionTarget;
        }
        else if (deltaTime > 0f)
        {
            occlusion = float.Lerp(occlusion, OcclusionTarget, Smoothing(deltaTime, OcclusionSmoothingSeconds));
        }
    }

    /// <summary>
    /// Pitches the track by how fast the listener and the sound are closing on each other. Both velocities
    /// are inferred from how far each moved since the last update - nothing here is told about movement.
    /// </summary>
    private void UpdateDoppler(in ListenerState listener, Vector3 direction, float distance)
    {
        if (dopplerTarget == null)
        {
            return;
        }

        if (listener.DeltaTime > 0f)
        {
            var instantVelocity = hasLastPosition ? (Position - lastPosition) / listener.DeltaTime : Vector3.Zero;

            if (instantVelocity.LengthSquared() > MaxSourceSpeed * MaxSourceSpeed)
            {
                instantVelocity = Vector3.Zero;
            }

            velocity = Vector3.Lerp(velocity, instantVelocity, Smoothing(listener.DeltaTime, VelocitySmoothingSeconds));
        }

        lastPosition = Position;
        hasLastPosition = true;

        // A sound in the listener's own head has no axis to close along, and the direction it falls back
        // to would read the listener's own movement as a shift
        if (DopplerScale == 0f || distance < MinDopplerDistance)
        {
            dopplerTarget.DopplerShift = 1f;
            return;
        }

        // Along the listener-to-sound axis: the listener moving towards the sound raises the pitch, the
        // sound moving away from the listener lowers it
        var closingRate = Vector3.Dot(listener.Velocity, direction);
        var recedingRate = Vector3.Dot(velocity, direction);

        var shift = Math.Clamp(1f + ((SpeedOfSound + closingRate) / (SpeedOfSound + recedingRate) - 1f) * DopplerScale, 0.5f, 2f);

        // Anything below this is inaudible as a pitch change, and it is worth snapping to exactly 1:
        // that is what lets a still listener keep streaming samples straight through instead of resampling
        dopplerTarget.DopplerShift = MathF.Abs(shift - 1f) < 0.001f ? 1f : shift;
    }

    /// <summary>
    /// Returns the lerp factor that reaches a fixed fraction of the way to the target in
    /// <paramref name="timeConstant"/> seconds no matter how <paramref name="deltaTime"/> is chopped up.
    /// </summary>
    private static float Smoothing(float deltaTime, float timeConstant)
    {
        return 1f - MathF.Exp(-deltaTime / timeConstant);
    }
}
