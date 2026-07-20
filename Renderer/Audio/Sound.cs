using ValveResourceFormat.Renderer.Audio;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Static entry point for sound event playback, usable from anywhere in the renderer: <c>Sound.Play("Base.Footstep", position)</c>.
/// All methods are safe to call when no <see cref="SoundEventPlayer"/> exists — they simply do nothing.
/// Internal so host applications never get a name collision with the vsnd resource type; they hold on to their <see cref="SoundEventPlayer"/> instance instead.
/// </summary>
internal static class Sound
{
    /// <summary>
    /// Gets the active sound event player. Set when a <see cref="SoundEventPlayer"/> is constructed, cleared when it is disposed.
    /// </summary>
    public static SoundEventPlayer? Player { get; internal set; }

    /// <summary>
    /// Plays a sound event by name.
    /// </summary>
    /// <param name="soundEventName">Name of the sound event, e.g. "Base.Footstep".</param>
    /// <param name="position">World position of the sound, or null for non-spatialized playback.</param>
    /// <param name="channel">Optional channel name (e.g. "player"). Playing on a channel stops whatever was playing on that channel before.</param>
    /// <param name="volume">Optional programmatic volume, replacing the definition's volume property.</param>
    /// <returns>A handle to the playing sound, or null when no player exists or the event could not be played.</returns>
    public static SoundEvent? Play(string soundEventName, Vector3? position = null, string? channel = null, float? volume = null)
        => Player?.Play(soundEventName, position, channel, volume);

    /// <summary>
    /// Stops the sound currently playing on the given channel, if any.
    /// </summary>
    public static void StopChannel(string channel) => Player?.StopChannel(channel);

    /// <summary>
    /// Pre-decodes every vsnd a sound event could play so the first <see cref="Play"/> does not hitch.
    /// Intended to warm the cache for a map's sound events at load time; blocks while decoding.
    /// </summary>
    public static void Cache(string soundEventName) => Player?.Cache(soundEventName);
}
