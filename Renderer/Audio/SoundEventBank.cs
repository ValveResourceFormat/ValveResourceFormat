using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>Stores sound event definitions loaded from soundevent (vsndevts) files.</summary>
public sealed class SoundEventBank
{
    // Sound event names are hashed case-insensitively by the engine (see StringToken), match that here.
    private readonly Dictionary<string, KVObject> soundEvents = new(capacity: 32768, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SoundEventDefinition> definitions = new(capacity: 2048, StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Drops a sound event, so playing it - or resolving it as another event's child - finds nothing.</summary>
    /// <param name="name">Name of the event to drop (case-insensitive).</param>
    /// <returns><see langword="true"/> if the bank held the event.</returns>
    public bool RemoveSoundEvent(string name)
    {
        definitions.Remove(name);
        return soundEvents.Remove(name);
    }

    /// <summary>Adds all sound event definitions from a parsed soundevents file.</summary>
    public void AddSoundEvents(KVObject soundEventsFile)
    {
        foreach (var soundEvent in soundEventsFile)
        {
            AddSoundEvent(soundEvent.Key, soundEventsFile.GetSubCollection(soundEvent.Key));
        }
    }

    /// <summary>Gets a sound event definition by name (case-insensitive), or null when unknown. Resolved and parsed once, then cached.</summary>
    public SoundEventDefinition? GetSoundEvent(string name)
    {
        if (definitions.TryGetValue(name, out var definition))
        {
            return definition;
        }

        if (!soundEvents.TryGetValue(name, out var soundEvent))
        {
            return null;
        }

        definition = new SoundEventDefinition(name, ResolveBase(name, soundEvent, depth: 0));
        definitions.Add(name, definition);
        return definition;
    }

    /// <summary>
    /// Resolves the "base" inheritance chain. A base is either a single event name, or an array
    /// of shared fragments referenced by "event_name" - a falloff curve, a mix send - each contributing only the fields the event lacks.
    /// The merged result replaces the stored definition, so resolution happens once per event.
    /// </summary>
    private KVObject ResolveBase(string name, KVObject soundEvent, int depth)
    {
        if (depth > SoundEvent.MaxRecursionDepth || !soundEvent.TryGetValue("base", out var baseValue))
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

        if (baseValue.ValueType == KVValueType.Array)
        {
            foreach (var fragment in soundEvent.GetArray("base"))
            {
                // The fragment's own "type" names what kind of fragment it is
                // ("citadel_distance_falloff"), not what the event is, so it is not inherited
                MergeBase(merged, fragment.GetStringProperty("event_name"), depth, inheritType: false);
            }
        }
        else
        {
            MergeBase(merged, soundEvent.GetStringProperty("base"), depth, inheritType: true);
        }

        soundEvents[name] = merged;
        return merged;
    }

    private void MergeBase(KVObject merged, string? baseName, int depth, bool inheritType)
    {
        if (string.IsNullOrEmpty(baseName) || !soundEvents.TryGetValue(baseName, out var baseEvent))
        {
            return;
        }

        foreach (var property in ResolveBase(baseName, baseEvent, depth + 1))
        {
            if (!inheritType && string.Equals(property.Key, "type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!merged.ContainsKey(property.Key))
            {
                merged.Add(property.Key, property.Value);
            }
        }
    }
}
