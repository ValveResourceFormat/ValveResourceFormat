using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A sound event definition with its properties parsed out of the key-values once.
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

    /// <summary>Gets the position baked into the definition. An all-zero authored position is a placeholder and parses as null.</summary>
    public Vector3? Position { get; }

    /// <summary>
    /// Gets whether child events play at this event's position ("set_child_position", e.g. a footstep's gear
    /// rustle follows the player), on by default for "csgo_mega". When false, children use their own authored positions.
    /// Read by the base <see cref="SoundEvent.StartAsChild"/>, so it lives here regardless of event type.
    /// </summary>
    public bool SetChildPosition { get; }

    /// <summary>Gets the offset added to the position (e.g. footsteps play 20 units above the ground).</summary>
    public Vector3 PositionOffset { get; }

    /// <summary>
    /// Gets the base volume, replaced by <see cref="SoundEvent.VolumeOverride"/> when set. Unit depends on the
    /// sub type: CS2 events author this linear (0-1); Deadlock events author it in decibels.
    /// </summary>
    public float Volume { get; }

    /// <summary>Gets the base playback rate multiplier.</summary>
    public float Pitch { get; }

    /// <summary>Gets the delay in seconds before the sound starts.</summary>
    public float Delay { get; }

    /// <summary>Gets the fade-in length in seconds, zero to start at full volume.</summary>
    public float FadeIn { get; }

    /// <summary>Gets the stop-fade length in seconds, zero to use the caller's fallback.</summary>
    public float FadeOut { get; }

    /// <summary>
    /// Gets how strongly geometry between the listener and the sound attenuates it
    /// ("occlusion_intensity", or "occlusion_scale" in hlvr): 0 (the default) is
    /// not occludable, 1 is fully muted when blocked.
    /// </summary>
    public float OcclusionIntensity { get; }

    /// <summary>
    /// Gets whether replays within <see cref="BlockDuration"/> are dropped ("block_matching_events",
    /// or "block_match_this_event" in hlvr).
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

    /// <summary>
    /// Fully stopped instances of this definition, waiting to be reused by the next
    /// <see cref="SoundEventPlayer.Play"/> instead of building a new instance (and its whole provider
    /// tree) per play. Created by the first play; grows to the definition's peak concurrency and stays
    /// there. Guarded by locking the list itself - instances are returned from the mixing thread
    /// (a sound running dry mid-read) as well as the game thread (explicit stops).
    /// </summary>
    internal List<SoundEvent>? IdlePool;

    internal SoundEventDefinition(string name, KVObject data)
    {
        Name = name;
        Data = data;

        Type = data.GetStringProperty("type", string.Empty);
        SetChildPosition = data.GetSoundBool("set_child_position", defaultValue: Type == "csgo_mega");

        // An all-zero position is an authoring placeholder (see "position_N" metadata) that the
        // map or game code is expected to fill in, not a real world position
        var position = data.GetSoundVector3("position");

        if (position != Vector3.Zero)
        {
            Position = position;
        }

        PositionOffset = data.GetSoundVector3("position_offset");

        Volume = data.GetSoundFloat("volume", 1f);
        Pitch = data.GetSoundFloat("pitch", 1f);
        Delay = data.GetSoundFloat("delay");
        FadeIn = data.GetSoundFloat("volume_fade_in");
        FadeOut = data.GetSoundFloat("volume_fade_out");
        // TODO: Deadlock types and "hlvr_2d_w_occlusion" default "occlusion_scale" to 1, which is not applied
        // because a blocked trace here mutes fully; confirm how strongly those types attenuate when occluded
        OcclusionIntensity = data.ContainsKey("occlusion_intensity")
            ? data.GetSoundFloat("occlusion_intensity")
            : data.GetSoundFloat("occlusion_scale");

        // TODO: "csgo_mega" and "choreo_3d" default "block_matching_events" to on (for 0.01s and 1s), which is
        // not applied: the block only matches the same entity or position, while this one is per definition
        // and would drop the same event playing at two places at once
        BlockMatchingEvents = data.GetSoundBool("block_matching_events") || data.GetSoundBool("block_match_this_event");
        BlockDuration = data.GetSoundFloat("block_duration");

        EnableRetrigger = data.GetSoundBool("enable_retrigger");
        RetriggerIntervalMin = data.GetSoundFloat("retrigger_interval_min", 1f);
        RetriggerIntervalMax = data.GetSoundFloat("retrigger_interval_max", 1f);
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Name} ({Type})");
}
