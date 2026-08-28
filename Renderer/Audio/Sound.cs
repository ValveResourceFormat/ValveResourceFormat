using ValveResourceFormat.Renderer.Audio;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Static entry point for sound event playback; safe to call when no <see cref="SoundEventPlayer"/> exists.
/// </summary>
internal static class Sound
{
    /// <summary>Gets the active sound event player. Set when constructed, cleared when disposed.</summary>
    public static SoundEventPlayer? Player { get; internal set; }

    /// <summary>
    /// Plays a sound event by name.
    /// </summary>
    /// <param name="soundEventName">Name of the sound event, e.g. "Base.Footstep".</param>
    /// <param name="position">World position of the sound, or null for non-spatialized playback.</param>
    /// <param name="channel">Optional channel name (e.g. "player"). Playing on a channel stops whatever was playing on that channel before.</param>
    /// <param name="volume">Optional programmatic volume, replacing the definition's volume property.</param>
    /// <returns>A handle to this play, or an inert one when no player exists or the event could not be played.</returns>
    public static SoundHandle Play(string soundEventName, Vector3? position = null, string? channel = null, float? volume = null)
        => Player?.Play(soundEventName, position, channel, volume) is { } soundEvent ? new SoundHandle(soundEvent) : default;

    /// <summary>
    /// Stops the sound currently playing on the given channel, if any.
    /// </summary>
    public static void StopChannel(string channel) => Player?.StopChannel(channel);

    /// <summary>Queues background decodes for every vsnd a sound event could play. Returns immediately.</summary>
    public static void Cache(string soundEventName) => Player?.Cache(soundEventName);

    /// <summary>Registers a soundscape region playing a single sound event, or null when there is no player.</summary>
    public static SoundEventPlayer.Soundscape? AddSoundscape(Vector3 position, float radius, string? soundEventName)
        => Player?.AddSoundscape(position, radius, soundEventName);

    /// <summary>Registers a classic scripted soundscape region, or null when there is no player.</summary>
    public static SoundEventPlayer.Soundscape? AddScriptedSoundscape(Vector3 position, float radius, string? soundscapeName)
        => Player?.AddScriptedSoundscape(position, radius, soundscapeName);
}
