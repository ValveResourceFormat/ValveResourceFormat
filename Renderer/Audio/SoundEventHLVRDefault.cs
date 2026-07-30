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

        if (data.ContainsKey("volume_falloff_max"))
        {
            distanceVolumeCurve = SoundEventCurve.Linear(
                data.GetFloatProperty("volume_falloff_min"), 1f,
                data.GetFloatProperty("volume_falloff_max"), 0f);
        }

        range = distanceVolumeCurve is { MaxX: > 0f }
            ? distanceVolumeCurve.MaxX
            : data.ContainsKey("distance_max") ? data.GetFloatProperty("distance_max")
            : data.ContainsKey("cull_at_distance") ? data.GetFloatProperty("cull_at_distance")
            : 1000f;

        limiterMax = (int)data.GetFloatProperty("limiter_max");
        limiterEnabled = data.GetBooleanProperty("limiter_on") && limiterMax > 0 && !data.GetBooleanProperty("limiter_match_entity");
        limiterKey = data.GetStringProperty("limiter_event_name", definition.Name);
        limiterStopOldest = data.GetBooleanProperty("limiter_stop_oldest");
    }

    protected override void DoStart()
    {
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;

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

        var soundName = trackNames[Mixer.Player.PickTrack(Definition, trackNames.Length)];
        var cachedSound = Mixer.Player.SoundCache.GetSound(soundName);
        PlayingSoundFile = soundName;

        if (cachedSound == null)
        {
            return;
        }

        var position = Position.HasValue ? Position.Value + PositionOffset : (Vector3?)null;
        // 2 interleaved stereo samples per frame
        var delaySamples = (int)(Definition.Delay * SampleRate) * 2;

        var sampleProvider = BuildTrackProvider(cachedSound, position, GetRandomizedPitch(), delaySamples);
        sampleProvider.Volume = GetRandomizedVolume();

        if (sampleProvider is SampleProvider3D spatial)
        {
            spatial.Range = range;
            spatial.DistanceVolumeCurve = distanceVolumeCurve;
        }

        SampleProviders.Add(sampleProvider);
    }

    internal override void Prewarm(int depth)
    {
        if (trackNames.Length == 0)
        {
            return;
        }

        foreach (var trackName in trackNames)
        {
            Mixer.Player.SoundCache.GetSound(trackName, background: true);
        }

        PrewarmTrackProvider(Mixer.Player.SoundCache.GetSound(trackNames[0], background: true));

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

    protected override void OnFinished()
    {
        base.OnFinished();

        // One-shot: fully stop once nothing in the tree can produce samples anymore, so the event
        // leaves the mixer's active set instead of staying registered (and updated) forever.
        if (!FadingOut)
        {
            Stop();
        }
    }

    private float GetRandomizedPitch()
    {
        var pitch = Definition.Pitch;

        if (pitchRandMin != 0f || pitchRandMax != 0f)
        {
            pitch += float.Lerp(pitchRandMin, pitchRandMax, Random.NextSingle());
        }

        return Math.Clamp(pitch, 0.25f, 4f);
    }

    private float GetRandomizedVolume()
    {
        var volume = VolumeOverride ?? Definition.Volume;

        if (volumeRandMin != 0f || volumeRandMax != 0f)
        {
            volume += float.Lerp(volumeRandMin, volumeRandMax, Random.NextSingle());
        }

        var mixGroupVolume = Mixer.Player.GetMixGroupVolume(mixGroup);

        return Math.Clamp(volume, 0f, 1f) * mixGroupVolume;
    }
}
