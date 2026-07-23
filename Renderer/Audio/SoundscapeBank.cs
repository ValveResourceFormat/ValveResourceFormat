using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Stores "scripted" soundscape definitions loaded from classic KeyValues1 soundscape script files
/// (e.g. "scripts/soundscapes_citadel.txt", or HLVR's "scripts/soundscapes_*.txt"), which name a set of
/// operators to start together for an <c>env_soundscape</c> region - the predecessor to the modern
/// single-sound-event soundscapes (<c>env_soundscape</c> with "enablesoundevent" set), which
/// <see cref="SoundEventPlayer.AddSoundscape"/> already handles directly.
/// </summary>
/// <remarks>
/// Operators:
/// <list type="bullet">
/// <item><c>playevent</c>: starts an existing named sound event directly.</item>
/// <item><c>playsoundscape</c>: inlines another soundscape's operators here.</item>
/// <item>
/// <c>playrandom</c>/<c>playlooping</c> (the older HLVR-style scripts, which define audio inline instead
/// of referencing a vsndevt): wrapped and registered as an anonymous entry in the main
/// <see cref="SoundEventBank"/> on first resolve, so the rest of the pipeline (caching, play, retrigger,
/// fade-out) treats them like any other named event. See <see cref="SoundEventScriptedRandom"/> and
/// <see cref="SoundEventScriptedLoop"/>.
/// </item>
/// </list>
/// DSP presets ("dsp") and per-operator repositioning ("traveler") are not modeled.
/// </remarks>
public sealed class SoundscapeBank
{
    private readonly SoundEventBank eventBank;

    // Soundscape names are referenced by map entities as plain strings, matched case-insensitively like everything else here
    private readonly Dictionary<string, KVObject> soundscapes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> resolved = new(StringComparer.OrdinalIgnoreCase);

    internal SoundscapeBank(SoundEventBank eventBank)
    {
        this.eventBank = eventBank;
    }

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
        var syntheticIndex = 0;
        Flatten(name, soundscape, events, visited, depth: 0, ref syntheticIndex);

        var result = events.ToArray();
        resolved[name] = result;
        return result;
    }

    private void Flatten(string ownerName, KVObject soundscape, List<string> events, HashSet<string> visited, int depth, ref int syntheticIndex)
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
                    Flatten(nestedName, nested, events, visited, depth + 1, ref syntheticIndex);
                }
            }
            else if (operation.Key.Equals("playrandom", StringComparison.OrdinalIgnoreCase))
            {
                events.Add(RegisterSynthetic(ownerName, "playrandom", "script_playrandom", operation.Value, ref syntheticIndex));
            }
            else if (operation.Key.Equals("playlooping", StringComparison.OrdinalIgnoreCase))
            {
                events.Add(RegisterSynthetic(ownerName, "playlooping", "script_playlooping", operation.Value, ref syntheticIndex));
            }
        }
    }

    /// <summary>
    /// Wraps an inline "playrandom"/"playlooping" operator block as an anonymous entry in the main
    /// <see cref="SoundEventBank"/>, under a synthetic name unique to this soundscape, tagged with a
    /// "type" so <see cref="SoundEvent.Build"/> dispatches it like any authored vsndevt. The operator's
    /// own key-values are nested under "operator" rather than merged into the wrapper directly: they use
    /// the same key names ("volume", "pitch", "position", ...) as the base vsndevt schema but with
    /// incompatible shapes (e.g. "position" "random" instead of a 3-float vector), which would otherwise
    /// trip up <see cref="SoundEventDefinition"/>'s own eager parsing of those keys.
    /// </summary>
    private string RegisterSynthetic(string ownerName, string operatorName, string type, KVObject data, ref int syntheticIndex)
    {
        var syntheticName = $"{ownerName}#{operatorName}{syntheticIndex++}";

        var wrapper = new KVObject();
        wrapper.Add("type", type);
        wrapper.Add("operator", data);

        eventBank.AddSoundEvent(syntheticName, wrapper);
        return syntheticName;
    }
}
