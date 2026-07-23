using System.Diagnostics;
using ValveResourceFormat.Renderer.Audio.SampleProviders;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "csgo_mega" sound event type: a random sound picked from a track list,
/// optional child sound events, and optional periodic retriggering.
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

    private bool wasInitialized;
    private bool waitingForRetrigger;
    private long retriggerTimestamp;

    private protected override bool WaitingToStart => waitingForRetrigger;
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
        // A position supplied by the caller (e.g. a point_soundevent entity) wins over the definition's
        // "position" key (all-zero placeholders are already dropped at parse time).
        if (Position == null && Definition.Position.HasValue)
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

        if (trackNames.Length > 0)
        {
            var soundName = trackNames[Mixer.Player.PickTrack(Definition, trackNames.Length)];
            var cachedSound = Mixer.Player.SoundCache.GetSound(soundName);
            PlayingSoundFile = soundName;

            if (cachedSound != null)
            {
                var position = Position.HasValue ? Position.Value + PositionOffset : (Vector3?)null;
                // 2 interleaved stereo samples per frame
                var delaySamples = (int)(Definition.Delay * SampleRate) * 2;

                var sampleProvider = BuildTrackProvider(cachedSound, position, GetRandomizedPitch(), delaySamples);
                sampleProvider.Volume = GetRandomizedVolume();

                if (sampleProvider is SampleProvider3D spatial)
                {
                    spatial.Range = range;
                    spatial.DistanceVolumeCurve = distanceVolumeCurve;
                    spatial.StereoMixCurve = stereoMixCurve;
                }

                SampleProviders.Add(sampleProvider);
            }
        }

        if (childEventNames.Length == 0)
        {
            return;
        }

        // Child definitions are resolved through the bank once and kept on the parent definition
        var childDefinitions = Definition.ChildDefinitions;
        if (childDefinitions == null)
        {
            childDefinitions = new SoundEventDefinition?[childEventNames.Length];

            for (var i = 0; i < childEventNames.Length; i++)
            {
                childDefinitions[i] = Mixer.Player.Bank.GetSoundEvent(childEventNames[i]);
            }

            Definition.ChildDefinitions = childDefinitions;
        }

        StartChildren(childDefinitions);
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

        // One-shot: fully stop once nothing in the tree can produce samples anymore, so the event
        // leaves the mixer's active set instead of staying registered (and updated) forever.
        // A child that is still started may be waiting on its own retrigger and keeps us alive.
        if (!AnyChildStarted())
        {
            Stop();
        }
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

    public override bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        if (Started && !FadingOut && waitingForRetrigger && Stopwatch.GetTimestamp() >= retriggerTimestamp)
        {
            waitingForRetrigger = false;
            Start();
        }

        return base.Update(listenerPosition, rightEarDirection);
    }

    private float GetRandomizedPitch()
    {
        var pitch = Definition.Pitch;

        if (pitchRandomMin != 0f || pitchRandomMax != 0f)
        {
            pitch += float.Lerp(pitchRandomMin, pitchRandomMax, Random.NextSingle());
        }

        return Math.Clamp(pitch, 0.25f, 4f);
    }

    private float GetRandomizedVolume()
    {
        // Events like Gear.JumpLand.CT have volume 0.0 in their data: the game passes the volume at play time
        var volume = VolumeOverride ?? Definition.Volume;

        if (volumeRandomMin != 0f || volumeRandomMax != 0f)
        {
            volume += float.Lerp(volumeRandomMin, volumeRandomMax, Random.NextSingle());
        }

        var mixGroupVolume = Mixer.Player.GetMixGroupVolume(mixGroup);

        return Math.Clamp(volume, 0f, 1f) * mixGroupVolume;
    }
}
