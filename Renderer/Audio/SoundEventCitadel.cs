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
    private bool wasInitialized;
    private bool waitingForRetrigger;
    private long retriggerTimestamp;

    private protected override bool WaitingToStart => waitingForRetrigger;

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

        volumeMult = data.GetFloatProperty("volume_mult", 1f);
        volumeFadeIn = data.GetFloatProperty("volume_fade_in");
        mixGroup = data.GetStringProperty("mixer_mixgroup", string.Empty);
        playAtListener = data.GetBooleanProperty("position_force_from_player");
        volumeOffsetDecibels = data.GetFloatProperty("volume_offset_player");

        var falloffCurve = SoundEventCurve.Parse(data, "volume_falloff_curve_db", decibels: true);

        if (falloffCurve != null && falloffCurve.Attenuates)
        {
            distanceVolumeCurve = falloffCurve;
        }
        else if (falloffCurve != null)
        {
            volumeCurveGain = falloffCurve.Evaluate(0f);
        }

        var falloffMax = GetPerspectiveFloat(data, "volume_falloff_max");

        if (distanceVolumeCurve == null && falloffMax > 0f)
        {
            distanceVolumeCurve = SoundEventCurve.Linear(
                GetPerspectiveFloat(data, "volume_falloff_min"), 1f,
                falloffMax, data.GetFloatProperty("volume_falloff_floor"));
        }

        // "spread_*" maps distance to stereo width, not to volume
        if (data.ContainsKey("spread_min") && data.ContainsKey("spread_max"))
        {
            stereoMixCurve = SoundEventCurve.Linear(
                data.GetFloatProperty("spread_min"), GetPerspectiveFloat(data, "spread_min_value", 1f),
                data.GetFloatProperty("spread_max"), GetPerspectiveFloat(data, "spread_max_value", 1f));
        }

        // An authored cull distance is the hard limit and wins over the curve's own extent. Each only
        // counts when positive: "max_distance_recipient_filter" is authored as -1 for "no limit", and a
        // zero would silence the event outright.
        var cullDistance = data.GetFloatProperty("cull_at_distance");
        var recipientDistance = data.GetFloatProperty("max_distance_recipient_filter");
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
        return data.ContainsKey(name + "_player")
            ? data.GetFloatProperty(name + "_player")
            : data.GetFloatProperty(name, fallback);
    }

    protected override void DoStart()
    {
        if (playAtListener)
        {
            // The emitter is pinned to the player, so it is always at zero distance: an unspatialized bed
            Position = null;
        }
        else if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;

        if (!wasInitialized && CheckRetrigger())
        {
            // Retriggered events wait out their first interval before playing
            wasInitialized = true;
            return;
        }

        wasInitialized = true;

        if (trackNames.Length == 0)
        {
            return;
        }

        // Definition.Volume is in decibels for this event family
        var decibels = (VolumeOverride ?? Definition.Volume) + volumeOffsetDecibels;
        var baseVolume = Math.Clamp(MathUtils.DecibelsToLinear(decibels), 0f, 1f);
        var mixGroupVolume = Mixer.Player.GetMixGroupVolume(mixGroup);
        targetVolume = baseVolume * volumeMult * volumeCurveGain * mixGroupVolume;

        fadeInSecondsRemaining = volumeFadeIn;
        startTimestamp = Stopwatch.GetTimestamp();

        trackProvider = StartTrack(trackNames, fadeInSecondsRemaining > 0f ? 0f : targetVolume, Definition.Pitch,
            range, distanceVolumeCurve, stereoMixCurve);
    }

    internal override void Prewarm(int depth) => PrewarmTracks(trackNames);

    internal override void ResetForReplay()
    {
        base.ResetForReplay();
        wasInitialized = false;
        waitingForRetrigger = false;
        fadeInSecondsRemaining = 0f;
    }

    protected override void OnFinished()
    {
        base.OnFinished();

        if (FadingOut)
        {
            // The base already completed the stop
            return;
        }

        if (CheckRetrigger())
        {
            return;
        }

        // One-shot (no retrigger): fully stop once nothing in the tree can produce samples anymore, so
        // the event leaves the mixer's active set instead of staying registered (and updated) forever.
        // A genuinely looping track (baked-in loop points) never reaches here.
        Stop();
    }

    private bool CheckRetrigger()
    {
        if (!Definition.EnableRetrigger)
        {
            return false;
        }

        var retriggerAt = float.Lerp(Definition.RetriggerIntervalMin, Definition.RetriggerIntervalMax, Random.NextSingle());
        retriggerTimestamp = Stopwatch.GetTimestamp() + (long)(retriggerAt * Stopwatch.Frequency);
        waitingForRetrigger = true;
        return true;
    }

    /// <inheritdoc/>
    public override bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        if (Started && !FadingOut && waitingForRetrigger && Stopwatch.GetTimestamp() >= retriggerTimestamp)
        {
            waitingForRetrigger = false;
            Start();
        }

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

        return base.Update(listenerPosition, rightEarDirection);
    }
}
