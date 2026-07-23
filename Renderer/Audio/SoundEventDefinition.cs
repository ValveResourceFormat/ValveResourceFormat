using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A sound event definition with its properties parsed out of the key-values once, on first play,
/// since reading a property off a <see cref="KVObject"/> allocates or boxes on every access.
/// Also carries mutable per-event playback state (last picked track, last play time).
/// Only holds properties genuinely shared across event types (same key name and meaning in every game's
/// schema) plus properties the base <see cref="SoundEvent"/>/<see cref="SoundEventPlayer"/> machinery itself
/// reads directly - everything else belongs on the type that understands it (e.g. <see cref="SoundEventCSGOMega"/>).
/// </summary>
public sealed class SoundEventDefinition
{
    /// <summary>Gets the sound event name this definition was loaded under.</summary>
    public string Name { get; }

    /// <summary>Gets the underlying key-values, with the "base" chain already merged in.</summary>
    public KVObject Data { get; }

    /// <summary>Gets the sound event type ("csgo_mega"), empty when the definition has none.</summary>
    public string Type { get; }

    /// <summary>Gets the position baked into the definition. An all-zero authored position is a placeholder and parses as null.</summary>
    public Vector3? Position { get; }

    /// <summary>
    /// Gets whether child events play at this event's position ("set_child_position", e.g. a footstep's gear
    /// rustle follows the player). When false - the common case - children use their own authored positions.
    /// Read by the base <see cref="SoundEvent.StartAsChild"/>, so it lives here regardless of event type.
    /// </summary>
    public bool SetChildPosition { get; }

    /// <summary>Gets the offset added to the position (e.g. footsteps play 20 units above the ground).</summary>
    public Vector3 PositionOffset { get; }

    /// <summary>
    /// Gets the base volume, replaced by <see cref="SoundEvent.VolumeOverride"/> when set. Unit depends on the
    /// event type: CS2 events author this linear (0-1); Deadlock events author it in decibels.
    /// </summary>
    public float Volume { get; }

    /// <summary>Gets the base playback rate multiplier.</summary>
    public float Pitch { get; }

    /// <summary>Gets the delay in seconds before the sound starts.</summary>
    public float Delay { get; }

    /// <summary>
    /// Gets how strongly geometry between the listener and the sound attenuates it
    /// ("occlusion_intensity"): 0 (the default) is not occludable, 1 is fully muted when blocked.
    /// </summary>
    public float OcclusionIntensity { get; }

    /// <summary>
    /// Gets whether replays within <see cref="BlockDuration"/> are dropped ("block_matching_events").
    /// </summary>
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

    /// <summary>
    /// Child definitions, resolved through the bank on first play and cached here so every instance/retrigger
    /// of this definition reuses the same resolution.
    /// </summary>
    internal SoundEventDefinition?[]? ChildDefinitions;

    internal SoundEventDefinition(string name, KVObject data)
    {
        Name = name;
        Data = data;

        Type = data.GetStringProperty("type", string.Empty);
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
        Pitch = data.GetFloatProperty("pitch", 1f);
        Delay = data.GetFloatProperty("delay");
        OcclusionIntensity = data.GetFloatProperty("occlusion_intensity");

        BlockMatchingEvents = data.GetBooleanProperty("block_matching_events");
        BlockDuration = data.GetFloatProperty("block_duration");

        EnableRetrigger = data.GetBooleanProperty("enable_retrigger");
        RetriggerIntervalMin = data.GetFloatProperty("retrigger_interval_min");
        RetriggerIntervalMax = data.GetFloatProperty("retrigger_interval_max");
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} ({Type})");
}
