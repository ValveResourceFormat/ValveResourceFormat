using System.Diagnostics;
using ValveKeyValue;
using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "citadel_default_2d", "citadel_default_3d", "citadel_ambient_3d" and
/// "citadel_emitter_lod" sound event types: a single track - either a looping ambience bed, or (when
/// "enable_retrigger" is set) a oneshot that replays itself on a randomized interval - with decibel
/// volume, a distance falloff and an optional fade-in on start.
/// </summary>
/// <remarks>
/// "citadel_emitter_lod" picks its take by listener distance ("vsnd_files_close"/"vsnd_files_far");
/// the close take is played, as the LOD switch itself is not modeled.
/// </remarks>
internal sealed class SoundEventCitadel : SoundEvent
{
    private readonly string[] trackNames;
    private readonly float volumeDecibels;
    private readonly float volumeMult;
    private readonly float volumeFadeIn;
    private readonly string mixGroup;
    private readonly SoundEventCurve? distanceVolumeCurve;
    private readonly SoundEventCurve? stereoMixCurve;
    private readonly bool playAtListener;
    private readonly float volumeOffsetDecibels;
    private readonly float volumeCurveGain = 1f;
    private readonly float range;

    // Types author their takes under their own key: a plain list, a distance LOD, or one per surface
    private static readonly string[] TrackKeys = [
        "vsnd_files",
        "vsnd_files_close",
        "vsnd_files_mid",
        "vsnd_files_far",
        "vsnd_files_footstep_default",
        "vsnd_files_impact_default",
    ];

    private AudioSampleProvider? trackProvider;
    private float targetVolume;
    private float fadeInSecondsRemaining;
    private long startTimestamp;

    public SoundEventCitadel(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        trackNames = [];

        foreach (var trackKey in TrackKeys)
        {
            trackNames = GetStringOrArrayProperty(data, trackKey);

            if (trackNames.Length > 0)
            {
                break;
            }
        }

        // In decibels for this event family, so an absent volume is unity gain rather than the shared linear 1
        volumeDecibels = data.GetSoundFloat("volume");
        volumeMult = data.GetSoundFloat("volume_mult", 1f);
        volumeFadeIn = data.GetSoundFloat("volume_fade_in");
        mixGroup = data.GetStringProperty("mixer_mixgroup", string.Empty);
        playAtListener = data.GetSoundBool("position_force_from_player",
            defaultValue: definition.Type is "citadel_default_2d" or "citadel_music");
        volumeOffsetDecibels = data.GetSoundFloat("volume_offset_player");

        var falloffCurve = SoundEventCurve.Parse(data, "volume_falloff_curve_db", decibels: true);

        if (falloffCurve != null && falloffCurve.Attenuates)
        {
            distanceVolumeCurve = falloffCurve;
        }
        else if (falloffCurve != null)
        {
            volumeCurveGain = falloffCurve.Evaluate(0f);
        }

        // TODO: When the event and its base fragments leave these out, the types default to a falloff
        // (800 to 3000 for most, 100 to 1200 for ambient_3d, a 250 minimum for hit_confirm and damage) and to
        // a spread of 0 at 250 to 0.1 at 500, neither of which is applied; confirm the curve shapes they drive
        var falloffMax = GetPerspectiveFloat(data, "volume_falloff_max");

        if (distanceVolumeCurve == null && falloffMax > 0f)
        {
            distanceVolumeCurve = SoundEventCurve.Linear(
                GetPerspectiveFloat(data, "volume_falloff_min"), 1f,
                falloffMax, data.GetSoundFloat("volume_falloff_floor"));
        }

        // "spread_*" maps distance to stereo width, not to volume
        if (data.ContainsKey("spread_min") && data.ContainsKey("spread_max"))
        {
            stereoMixCurve = SoundEventCurve.Linear(
                data.GetSoundFloat("spread_min"), GetPerspectiveFloat(data, "spread_min_value", 1f),
                data.GetSoundFloat("spread_max"), GetPerspectiveFloat(data, "spread_max_value", 1f));
        }

        // An authored cull distance is the hard limit and wins over the curve's own extent. Each only
        // counts when positive: "max_distance_recipient_filter" is authored as -1 for "no limit", and a
        // zero would silence the event outright.
        var cullDistance = data.GetSoundFloat("cull_at_distance");
        var recipientDistance = data.GetSoundFloat("max_distance_recipient_filter");
        var audibleLimit = cullDistance > 0f ? cullDistance : recipientDistance > 0f ? recipientDistance : 0f;

        if (audibleLimit > 0f && distanceVolumeCurve != null)
        {
            distanceVolumeCurve = distanceVolumeCurve.WithCutoff(audibleLimit);
        }

        range = audibleLimit > 0f ? audibleLimit
            : distanceVolumeCurve is { MaxX: > 0f } ? distanceVolumeCurve.MaxX
            : 512f;
    }

    /// <summary>
    /// Reads a property the game authors once per listener relationship, taking the local player's variant
    /// when there is one: the viewer is always the player, never a team mate or an opponent.
    /// </summary>
    private static float GetPerspectiveFloat(KVObject data, string name, float fallback = 0f)
    {
        var playerName = $"{name}_player";

        return data.ContainsKey(playerName)
            ? data.GetSoundFloat(playerName)
            : data.GetSoundFloat(name, fallback);
    }

    protected override void DoStart()
    {
        if (playAtListener)
        {
            // The emitter is pinned to the player, so it is always at zero distance: an unspatialized bed
            Position = null;
        }

        if (WaitOutFirstInterval())
        {
            return;
        }

        if (trackNames.Length == 0)
        {
            return;
        }

        var decibels = (VolumeOverride ?? volumeDecibels) + volumeOffsetDecibels;
        var baseVolume = MathUtils.Saturate(MathUtils.DecibelsToLinear(decibels));
        var mixGroupVolume = Mixer.Player.GetMixGroupVolume(mixGroup);
        targetVolume = baseVolume * volumeMult * volumeCurveGain * mixGroupVolume * VolumeScale;

        fadeInSecondsRemaining = volumeFadeIn;
        startTimestamp = Stopwatch.GetTimestamp();

        trackProvider = StartTrack(trackNames, fadeInSecondsRemaining > 0f ? 0f : targetVolume, Definition.Pitch,
            range, distanceVolumeCurve, stereoMixCurve);
    }

    internal override void Prewarm(int depth) => PrewarmTracks(trackNames);

    internal override void ResetForReplay()
    {
        base.ResetForReplay();
        fadeInSecondsRemaining = 0f;
    }

    private protected override bool StayAliveAfterFinishing() => CheckRetrigger();

    /// <inheritdoc/>
    public override bool Update(in ListenerState listener)
    {
        if (trackProvider != null && fadeInSecondsRemaining > 0f)
        {
            var elapsed = (float)Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;

            if (elapsed >= fadeInSecondsRemaining)
            {
                trackProvider.Volume = targetVolume;
                fadeInSecondsRemaining = 0f;
            }
            else
            {
                trackProvider.Volume = targetVolume * (elapsed / fadeInSecondsRemaining);
            }
        }

        return base.Update(listener);
    }
}
