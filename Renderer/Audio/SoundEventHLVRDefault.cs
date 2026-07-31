using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_default_3d" and "hlvr_2d_w_occlusion" sound event types,
/// plus the simpler legacy "src1_3d"/"src1_2d" types (ported Source 1 game_sounds.txt entries) which
/// share the same volume/pitch/randomization/mixgroup/delay keys and just skip the HLVR-only ones
/// (occlusion, distance falloff curve): a one-shot random track pick with volume/pitch jitter and an
/// optional linear distance falloff.
/// </summary>
internal sealed class SoundEventHLVRDefault : SoundEvent
{
    private readonly string[] trackNames;
    private readonly string mixGroup;
    private readonly float volumeRandMin;
    private readonly float volumeRandMax;
    private readonly float pitchRandMin;
    private readonly float pitchRandMax;
    private readonly SoundEventCurve? distanceVolumeCurve;
    private readonly float range;

    // "limiter_*": caps how many instances of this event (or a shared "limiter_event_name" group) can
    // play at once. Only the global form is applied - "limiter_match_entity" (by far the more common
    // authoring, e.g. "at most 1 flesh impact per NPC") needs a per-emitter concept this engine doesn't
    // have, so those definitions are left unbounded rather than risk an incorrectly global cap.
    private readonly bool limiterEnabled;
    private readonly string limiterKey;
    private readonly int limiterMax;
    private readonly bool limiterStopOldest;
    private bool limiterRegistered;

    public SoundEventHLVRDefault(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        trackNames = GetStringOrArrayProperty(data, "vsnd_files");
        mixGroup = data.GetStringProperty("mixgroup", string.Empty);

        volumeRandMin = data.GetFloatProperty("volume_rand_min");
        volumeRandMax = data.GetFloatProperty("volume_rand_max");
        pitchRandMin = data.GetFloatProperty("pitch_rand_min");
        pitchRandMax = data.GetFloatProperty("pitch_rand_max");

        // VO lines author the falloff as a curve instead of a min/max pair
        distanceVolumeCurve = SoundEventCurve.Parse(data, "volume_falloff_curve");

        if (distanceVolumeCurve == null && data.ContainsKey("volume_falloff_max"))
        {
            distanceVolumeCurve = SoundEventCurve.Linear(
                data.GetFloatProperty("volume_falloff_min"), 1f,
                data.GetFloatProperty("volume_falloff_max"), 0f);
        }

        // Only positive values are a limit: a zero (or a missing key read as zero) would silence the event
        var distanceMax = data.GetFloatProperty("distance_max");
        var cullDistance = data.GetFloatProperty("cull_at_distance");

        range = distanceVolumeCurve is { MaxX: > 0f } ? distanceVolumeCurve.MaxX
            : distanceMax > 0f ? distanceMax
            : cullDistance > 0f ? cullDistance
            : 1000f;

        limiterMax = (int)data.GetFloatProperty("limiter_max");
        limiterEnabled = data.GetBooleanProperty("limiter_on") && limiterMax > 0 && !data.GetBooleanProperty("limiter_match_entity");
        limiterKey = data.GetStringProperty("limiter_event_name", definition.Name);
        limiterStopOldest = data.GetBooleanProperty("limiter_stop_oldest");
    }

    protected override void DoStart()
    {
        if (trackNames.Length == 0)
        {
            return;
        }

        if (limiterEnabled)
        {
            EnsureLimiterRegistered();

            if (Mixer.Player.GetLimiterCount(limiterKey) >= limiterMax)
            {
                if (!limiterStopOldest)
                {
                    // At the cap and not allowed to steal a slot: drop this instance rather than play over it
                    return;
                }

                Mixer.Player.StopOldestLimiterInstance(limiterKey);
            }
        }

        StartTrack(trackNames,
            GetRandomizedVolume(volumeRandMin, volumeRandMax, mixGroup),
            GetRandomizedPitch(pitchRandMin, pitchRandMax),
            range, distanceVolumeCurve);
    }

    internal override void Prewarm(int depth)
    {
        if (trackNames.Length == 0)
        {
            return;
        }

        PrewarmTracks(trackNames);

        if (limiterEnabled)
        {
            EnsureLimiterRegistered();
        }
    }

    private void EnsureLimiterRegistered()
    {
        if (!limiterRegistered)
        {
            limiterRegistered = true;
            Mixer.Player.RegisterLimiterGroup(this, limiterKey);
        }
    }
}
