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

    public int Count => soundEvents.Count;

    public void AddSoundEvent(string name, KVObject soundEventData)
    {
        if (soundEventData != null)
        {
            soundEvents.TryAdd(name, soundEventData);
        }
    }

    public void AddSoundEvents(KVObject soundEventsFile)
    {
        foreach (var soundEvent in soundEventsFile)
        {
            AddSoundEvent(soundEvent.Key, soundEventsFile.GetSubCollection(soundEvent.Key));
        }
    }

    public KVObject? GetSoundEvent(string name)
    {
        return soundEvents.TryGetValue(name, out var soundEvent) ? soundEvent : null;
    }
}
