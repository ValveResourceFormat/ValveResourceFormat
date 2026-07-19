using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Stores sound event definitions loaded from soundevent (vsndevts) files.
/// </summary>
public sealed class SoundEventBank
{
    // Sound event names are hashed case-insensitively by the engine (see StringToken), match that here
    private readonly Dictionary<string, KVObject> soundEvents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the number of loaded sound event definitions.</summary>
    public int Count => soundEvents.Count;

    /// <summary>Adds a single sound event definition. An existing definition with the same name is kept.</summary>
    public void AddSoundEvent(string name, KVObject soundEventData)
    {
        if (soundEventData != null)
        {
            soundEvents.TryAdd(name, soundEventData);
        }
    }

    /// <summary>Adds all sound event definitions from a parsed soundevents file.</summary>
    public void AddSoundEvents(KVObject soundEventsFile)
    {
        foreach (var soundEvent in soundEventsFile)
        {
            AddSoundEvent(soundEvent.Key, soundEventsFile.GetSubCollection(soundEvent.Key));
        }
    }

    /// <summary>Gets a sound event definition by name (case-insensitive), or null when unknown.</summary>
    public KVObject? GetSoundEvent(string name)
    {
        return soundEvents.TryGetValue(name, out var soundEvent) ? soundEvent : null;
    }
}
