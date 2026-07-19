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
        return soundEvents.TryGetValue(name, out var soundEvent)
            ? ResolveBase(name, soundEvent, depth: 0)
            : null;
    }

    /// <summary>
    /// Resolves the "base" inheritance chain: most events (e.g. CT_Concrete.StepLeft) only override a few
    /// properties of a base event (e.g. Base.Footstep) which carries the type and the rest of the data.
    /// The merged result replaces the stored definition, so resolution happens once per event.
    /// </summary>
    private KVObject ResolveBase(string name, KVObject soundEvent, int depth)
    {
        var baseName = soundEvent.GetStringProperty("base");

        if (baseName == null || depth > 8)
        {
            return soundEvent;
        }

        var merged = new KVObject();

        foreach (var property in soundEvent)
        {
            if (!string.Equals(property.Key, "base", StringComparison.OrdinalIgnoreCase))
            {
                merged.Add(property.Key, property.Value);
            }
        }

        if (soundEvents.TryGetValue(baseName, out var baseEvent))
        {
            foreach (var property in ResolveBase(baseName, baseEvent, depth + 1))
            {
                if (!merged.ContainsKey(property.Key))
                {
                    merged.Add(property.Key, property.Value);
                }
            }
        }

        soundEvents[name] = merged;
        return merged;
    }
}
