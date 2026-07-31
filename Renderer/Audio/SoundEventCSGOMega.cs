using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "csgo_mega" sound event type, and Half-Life: Alyx's "choreo_3d", whose keys are a
/// subset of it: a random sound picked from a track list, optional child sound events, and optional
/// periodic retriggering.
/// </summary>
internal sealed class SoundEventCSGOMega : SoundEvent
{
    private readonly string[] trackNames;
    private readonly string[] childEventNames;
    private readonly float volumeRandomMin;
    private readonly float volumeRandomMax;
    private readonly float pitchRandomMin;
    private readonly float pitchRandomMax;
    private readonly string mixGroup;
    private readonly SoundEventCurve? distanceVolumeCurve;
    private readonly SoundEventCurve? stereoMixCurve;
    private readonly SoundEventCurve? fadeOutCurve;
    private readonly float range;

    private protected override SoundEventCurve? FadeOutCurve => fadeOutCurve;

    public SoundEventCSGOMega(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        trackNames = GetStringOrArrayProperty(data, "vsnd_files_track_01");
        childEventNames = data.GetBooleanProperty("enable_child_events")
            ? GetStringOrArrayProperty(data, "soundevent_01")
            : [];

        volumeRandomMin = data.GetFloatProperty("volume_random_min");
        volumeRandomMax = data.GetFloatProperty("volume_random_max");
        pitchRandomMin = data.GetFloatProperty("pitch_random_min");
        pitchRandomMax = data.GetFloatProperty("pitch_random_max");
        mixGroup = data.GetStringProperty("mixgroup", string.Empty);

        // Not gated on the "use_" flags: the vast majority of events carry these curves without the flag
        // set (e.g. soundscape ambients author a flat 1.0 distance curve and no flag), and the game
        // audibly honors them - the flag governs a different runtime path.
        var volumeCurve = SoundEventCurve.Parse(data, "distance_volume_mapping_curve");
        distanceVolumeCurve = volumeCurve;
        stereoMixCurve = SoundEventCurve.Parse(data, "distance_unfiltered_stereo_mapping_curve");

        // Not gated on "use_fadetime_volume_mapping_curve": that flag governs a different runtime path,
        // authored fade curves are used for stop fades whenever present
        fadeOutCurve = SoundEventCurve.Parse(data, "fadetime_volume_mapping_curve");

        // Only reached by the fallback falloff when the event has no volume curve
        range = volumeCurve is { MaxX: > 0f } ? volumeCurve.MaxX : 1000f;
    }

    protected override void DoStart()
    {
        if (WaitOutFirstInterval())
        {
            return;
        }

        StartTrack(trackNames,
            GetRandomizedVolume(volumeRandomMin, volumeRandomMax, mixGroup),
            GetRandomizedPitch(pitchRandomMin, pitchRandomMax),
            range, distanceVolumeCurve, stereoMixCurve);

        if (childEventNames.Length == 0)
        {
            return;
        }

        StartChildren(Definition.ChildDefinitions ??= ResolveChildDefinitions(childEventNames));
    }

    internal override void Prewarm(int depth)
    {
        PrewarmTracks(trackNames);

        if (childEventNames.Length > 0)
        {
            PrewarmChildren(Definition.ChildDefinitions ??= ResolveChildDefinitions(childEventNames), depth);
        }
    }

    private protected override bool StayAliveAfterFinishing() => CheckRetrigger() || AnyChildStarted();
}
