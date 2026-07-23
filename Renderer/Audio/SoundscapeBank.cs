using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Stores "scripted" soundscape definitions loaded from classic KeyValues1 soundscape script files
/// (e.g. "scripts/soundscapes_citadel.txt"), which name a set of sound events to start together for an
/// <c>env_soundscape</c> region - the predecessor to the modern single-sound-event soundscapes
/// (<c>env_soundscape</c> with "enablesoundevent" set), which <see cref="SoundEventPlayer.AddSoundscape"/>
/// already handles directly.
/// </summary>
/// <remarks>
/// Each named soundscape is a list of "playevent" (start this sound event) and "playsoundscape" (inline
/// another soundscape's operators here) entries; there is no operator to pick just one, so every operator
/// resolves to a sound event that is started - any occasional/randomized feel comes from the individual
/// sound events' own "enable_retrigger" behavior, not from the soundscape script itself.
/// DSP presets ("dsp") and per-operator repositioning ("traveler") are not modeled.
/// </remarks>
public sealed class SoundscapeBank
{
    // Soundscape names are referenced by map entities as plain strings, matched case-insensitively like everything else here
    private readonly Dictionary<string, KVObject> soundscapes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> resolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds all soundscape definitions from a parsed soundscape script's root object.</summary>
    public void AddSoundscapes(KVObject scriptRoot)
    {
        foreach (var soundscape in scriptRoot)
        {
            soundscapes.TryAdd(soundscape.Key, soundscape.Value);
        }
    }

    /// <summary>
    /// Gets the flattened list of sound event names a scripted soundscape starts, recursively inlining
    /// "playsoundscape" references, or null when the soundscape is unknown. Resolved once, then cached.
    /// </summary>
    public string[]? GetSoundEvents(string name)
    {
        if (resolved.TryGetValue(name, out var cached))
        {
            return cached;
        }

        if (!soundscapes.TryGetValue(name, out var soundscape))
        {
            return null;
        }

        var events = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        Flatten(soundscape, events, visited, depth: 0);

        var result = events.ToArray();
        resolved[name] = result;
        return result;
    }

    private void Flatten(KVObject soundscape, List<string> events, HashSet<string> visited, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        foreach (var operation in soundscape)
        {
            if (operation.Key.Equals("playevent", StringComparison.OrdinalIgnoreCase))
            {
                var eventName = operation.Value.GetStringProperty("event");

                if (eventName != null)
                {
                    events.Add(eventName);
                }
            }
            else if (operation.Key.Equals("playsoundscape", StringComparison.OrdinalIgnoreCase))
            {
                var nestedName = operation.Value.GetStringProperty("name");

                if (nestedName != null && visited.Add(nestedName) && soundscapes.TryGetValue(nestedName, out var nested))
                {
                    Flatten(nested, events, visited, depth + 1);
                }
            }
        }
    }
}
