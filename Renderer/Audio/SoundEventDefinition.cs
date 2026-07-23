using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A sound event definition with its properties parsed out of the key-values once, on first play,
/// since reading a property off a <see cref="KVObject"/> allocates or boxes on every access.
/// Also carries mutable per-event playback state (last picked track, last play time).
/// </summary>
public sealed class SoundEventDefinition
{
    /// <summary>Gets the sound event name this definition was loaded under.</summary>
    public string Name { get; }

    /// <summary>Gets the underlying key-values, with the "base" chain already merged in.</summary>
    public KVObject Data { get; }

    /// <summary>Gets the sound event type ("csgo_mega"), empty when the definition has none.</summary>
    public string Type { get; }

    /// <summary>Gets the vsnd files to pick a track from ("vsnd_files_track_01").</summary>
    public string[] TrackNames { get; }

    /// <summary>Gets the child sound event names, empty unless "enable_child_events" is set.</summary>
    public string[] ChildEventNames { get; }

    /// <summary>Gets the position baked into the definition. An all-zero authored position is a placeholder and parses as null.</summary>
    public Vector3? Position { get; }

    /// <summary>
    /// Gets whether child events play at this event's position ("set_child_position", e.g. a footstep's gear
    /// rustle follows the player). When false - the common case - children use their own authored positions.
    /// </summary>
    public bool SetChildPosition { get; }

    /// <summary>Gets the offset added to the position (e.g. footsteps play 20 units above the ground).</summary>
    public Vector3 PositionOffset { get; }

    /// <summary>Gets the base volume, replaced by <see cref="SoundEvent.VolumeOverride"/> when set.</summary>
    public float Volume { get; }
    /// <summary>Gets the lower bound of the random volume offset.</summary>
    public float VolumeRandomMin { get; }
    /// <summary>Gets the upper bound of the random volume offset.</summary>
    public float VolumeRandomMax { get; }

    /// <summary>Gets the base playback rate multiplier.</summary>
    public float Pitch { get; }
    /// <summary>Gets the lower bound of the random pitch offset.</summary>
    public float PitchRandomMin { get; }
    /// <summary>Gets the upper bound of the random pitch offset.</summary>
    public float PitchRandomMax { get; }

    /// <summary>Gets the delay in seconds before the sound starts.</summary>
    public float Delay { get; }

    /// <summary>
    /// Gets how strongly geometry between the listener and the sound attenuates it
    /// ("occlusion_intensity"): 0 (the default) is not occludable, 1 is fully muted when blocked.
    /// </summary>
    public float OcclusionIntensity { get; }

    /// <summary>Gets the mix group name, empty when the definition has none.</summary>
    public string MixGroup { get; }

    /// <summary>Gets the distance to volume mapping curve, null unless the matching "use_" flag is set.</summary>
    public SoundEventCurve? DistanceVolumeCurve { get; }

    /// <summary>Gets the distance to unfiltered-stereo mapping curve, null unless the matching "use_" flag is set.</summary>
    public SoundEventCurve? StereoMixCurve { get; }

    /// <summary>
    /// Gets the fade-out curve (seconds to volume) applied when the event is stopped with a fade,
    /// e.g. a soundscape being left ("fadetime_volume_mapping_curve").
    /// </summary>
    public SoundEventCurve? FadeTimeVolumeCurve { get; }

    /// <summary>Gets the audible range: the largest distance in the volume curve, or 1000 when there is none.</summary>
    public float Range { get; }

    /// <summary>Gets whether replays within <see cref="BlockDuration"/> are dropped ("block_matching_events").</summary>
    public bool BlockMatchingEvents { get; }
    /// <summary>Gets how long replays are blocked for, in seconds.</summary>
    public float BlockDuration { get; }

    /// <summary>Gets whether the event replays itself on an interval.</summary>
    public bool EnableRetrigger { get; }
    /// <summary>Gets the lower bound of the retrigger interval, in seconds.</summary>
    public float RetriggerIntervalMin { get; }
    /// <summary>Gets the upper bound of the retrigger interval, in seconds.</summary>
    public float RetriggerIntervalMax { get; }

    /// <summary>
    /// The last track index picked for this event, or -1 when it has never played.
    /// Kept here rather than in a table keyed by the definition so repeat plays touch nothing but this field.
    /// </summary>
    internal int LastTrackIndex = -1;

    /// <summary>Stopwatch timestamp of the last play, for <see cref="BlockMatchingEvents"/>.</summary>
    internal long LastPlayedTimestamp;

    /// <summary>Child definitions, resolved through the bank on first play.</summary>
    internal SoundEventDefinition?[]? ChildDefinitions;

    internal SoundEventDefinition(string name, KVObject data)
    {
        Name = name;
        Data = data;

        Type = data.GetStringProperty("type", string.Empty);
        TrackNames = GetStringArray(data, "vsnd_files_track_01");
        ChildEventNames = data.GetBooleanProperty("enable_child_events")
            ? GetStringArray(data, "soundevent_01")
            : [];
        SetChildPosition = data.GetBooleanProperty("set_child_position");

        if (data.ContainsKey("position"))
        {
            var position = new Vector3(data.GetFloatArray("position"));

            // An all-zero position is an authoring placeholder (see "position_N" metadata) that the
            // map or game code is expected to fill in, not a real world position
            if (position != Vector3.Zero)
            {
                Position = position;
            }
        }

        if (data.ContainsKey("position_offset"))
        {
            PositionOffset = new Vector3(data.GetFloatArray("position_offset"));
        }

        Volume = data.GetFloatProperty("volume", 1f);
        VolumeRandomMin = data.GetFloatProperty("volume_random_min");
        VolumeRandomMax = data.GetFloatProperty("volume_random_max");

        Pitch = data.GetFloatProperty("pitch", 1f);
        PitchRandomMin = data.GetFloatProperty("pitch_random_min");
        PitchRandomMax = data.GetFloatProperty("pitch_random_max");

        Delay = data.GetFloatProperty("delay");
        OcclusionIntensity = data.GetFloatProperty("occlusion_intensity");
        MixGroup = data.GetStringProperty("mixgroup", string.Empty);

        // Not gated on the "use_" flags: the vast majority of events carry these curves without the flag
        // set (e.g. soundscape ambients author a flat 1.0 distance curve and no flag), and the game
        // audibly honors them - the flag governs a different runtime path.
        var volumeCurve = SoundEventCurve.Parse(data, "distance_volume_mapping_curve");
        DistanceVolumeCurve = volumeCurve;
        StereoMixCurve = SoundEventCurve.Parse(data, "distance_unfiltered_stereo_mapping_curve");

        // Not gated on "use_fadetime_volume_mapping_curve": that flag governs a different runtime path,
        // authored fade curves are used for stop fades whenever present
        FadeTimeVolumeCurve = SoundEventCurve.Parse(data, "fadetime_volume_mapping_curve");

        // Only reached by the fallback falloff when the event has no volume curve
        Range = volumeCurve is { MaxX: > 0f } ? volumeCurve.MaxX : 1000f;

        BlockMatchingEvents = data.GetBooleanProperty("block_matching_events");
        BlockDuration = data.GetFloatProperty("block_duration");

        EnableRetrigger = data.GetBooleanProperty("enable_retrigger");
        RetriggerIntervalMin = data.GetFloatProperty("retrigger_interval_min");
        RetriggerIntervalMax = data.GetFloatProperty("retrigger_interval_max");
    }

    /// <summary>Reads a property that is either an array of strings or a single string.</summary>
    private static string[] GetStringArray(KVObject data, string name)
    {
        if (!data.TryGetValue(name, out var value))
        {
            return [];
        }

        if (value.ValueType == KVValueType.Array)
        {
            return data.GetArray<string>(name) ?? [];
        }

        var single = data.GetStringProperty(name);
        return single != null ? [single] : [];
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} ({Type})");
}
