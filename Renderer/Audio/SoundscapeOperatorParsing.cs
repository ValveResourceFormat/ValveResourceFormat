using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Shared parsing helpers for classic soundscape script operators ("playrandom", "playlooping"): the
/// old KeyValues1 format authors ranges as a single "min, max" string and named sound levels
/// ("SNDLVL_140db") instead of the KV3 vsndevt schema's separate min/max keys and raw floats.
/// </summary>
internal static class SoundscapeOperatorParsing
{
    /// <summary>
    /// Parses a "min, max" (or a single "value") property into a range. Malformed or missing values
    /// fall back to <paramref name="defaultValue"/> for both ends rather than throwing - scripted
    /// soundscapes are hand-authored text and do show up with typos.
    /// </summary>
    public static (float Min, float Max) ParseRange(KVObject data, string key, float defaultValue)
    {
        var text = data.GetStringProperty(key);

        if (string.IsNullOrEmpty(text))
        {
            return (defaultValue, defaultValue);
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
        {
            return (min, max);
        }

        if (parts.Length == 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var single))
        {
            return (single, single);
        }

        return (defaultValue, defaultValue);
    }

    /// <summary>
    /// Collects every "wave" entry under a "rndwave" sub-block (repeated sibling keys, not a KV3 array),
    /// or an empty list when there is no rndwave block.
    /// </summary>
    public static string[] GetRandomWaveFiles(KVObject data)
    {
        if (!data.TryGetValue("rndwave", out var rndwave))
        {
            return [];
        }

        var waves = new List<string>();

        foreach (var entry in rndwave)
        {
            if (entry.Key.Equals("wave", StringComparison.OrdinalIgnoreCase))
            {
                var wave = (string)entry.Value;

                if (!string.IsNullOrEmpty(wave))
                {
                    waves.Add(wave);
                }
            }
        }

        return [.. waves];
    }

    /// <summary>
    /// Converts a "soundlevel" token ("SNDLVL_140db", a named constant like "SNDLVL_NORM", or a bare
    /// number) into an approximate audible range in world units, or <paramref name="fallbackRange"/> when
    /// missing or unrecognized. This is a rough heuristic (6 dB falloff per doubling of distance from a
    /// "normal conversation" reference), not the engine's real attenuation curve - just enough that a
    /// gunfire-level entry carries further than a quiet drip.
    /// </summary>
    public static float SoundLevelToRange(string? token, float fallbackRange)
    {
        if (string.IsNullOrEmpty(token))
        {
            return fallbackRange;
        }

        var suffix = token.StartsWith("SNDLVL_", StringComparison.OrdinalIgnoreCase)
            ? token["SNDLVL_".Length..]
            : token;

        float db;

        if (suffix.EndsWith("db", StringComparison.OrdinalIgnoreCase)
            && float.TryParse(suffix[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDb))
        {
            db = parsedDb;
        }
        else if (!NamedSoundLevels.TryGetValue(suffix, out db)
            && !float.TryParse(suffix, NumberStyles.Float, CultureInfo.InvariantCulture, out db))
        {
            return fallbackRange;
        }

        var range = ReferenceRange * MathF.Pow(2f, (db - ReferenceDb) / 6f);
        return Math.Clamp(range, MinRange, MaxRange);
    }

    private static readonly Dictionary<string, float> NamedSoundLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NONE"] = 0f,
        ["IDLE"] = 60f,
        ["STATIC"] = 66f,
        ["NORM"] = 75f,
        ["TALKING"] = 80f,
        ["SINGING"] = 80f,
        ["GUNFIRE"] = 140f,
        ["WEAPON"] = 150f,
    };

    // "Normal conversation" (SNDLVL_NORM) audible out to a modest ambient range; every +6 dB doubles it.
    private const float ReferenceDb = 75f;
    private const float ReferenceRange = 400f;
    private const float MinRange = 100f;
    private const float MaxRange = 4000f;
}
