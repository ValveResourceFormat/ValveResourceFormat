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

        range = distanceVolumeCurve is { MaxX: > 0f } ? distanceVolumeCurve.MaxX : 1000f;
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
