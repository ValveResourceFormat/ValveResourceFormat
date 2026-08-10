using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Shared parsing helpers for Source 1 soundscape script operators ("playrandom", "playlooping"): the
/// old KeyValues1 format authors ranges as a single "min, max" string and named sound levels ("SNDLVL_140db").
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
    /// Parses a classic soundscape operator's "origin" ("x, y, z", occasionally with a stray trailing
    /// semicolon left over from hand-authored scripts) into a world position, or null when the key is
    /// missing or malformed. Unlike the modern vsndevt schema's "position" (a KV3 float array), this is a
    /// single comma-separated string.
    /// </summary>
    public static Vector3? ParseOrigin(KVObject data, string key = "origin")
    {
        var text = data.GetStringProperty(key);

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 3)
        {
            return null;
        }

        Span<float> values = stackalloc float[3];

        for (var i = 0; i < 3; i++)
        {
            var part = parts[i].TrimEnd(';', ' ');

            if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        return new Vector3(values[0], values[1], values[2]);
    }

    /// <summary>
    /// Collects every "wave" entry under a "rndwave" sub-block, or an empty list when there is no rndwave block.
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
    /// number) into the engine's distance multiplier: the reciprocal of the distance the sound stays at
    /// full volume out to, past which it follows the inverse distance law (see
    /// <see cref="SampleProviders.SampleProvider3D.DistanceMult"/>). Zero means it never attenuates
    /// (SNDLVL_NONE). Missing and unrecognized tokens fall back to <paramref name="fallbackDecibels"/>.
    /// </summary>
    /// <remarks>
    /// A sound level is the sound pressure the source is authored at, so the model is the physical one the
    /// engine uses: a source at <see cref="ReferenceDecibels"/> is played at full volume out to
    /// <see cref="ReferenceDistance"/> units, and every doubling of distance past that halves the
    /// amplitude. It never reaches zero - which is the point of it, a loud sound stays faintly audible far
    /// past where a "range" would have cut it off entirely.
    /// </remarks>
    public static float SoundLevelToDistanceMult(string? token, float fallbackDecibels = 75f)
    {
        var db = ParseSoundLevel(token) ?? fallbackDecibels;

        if (db <= 0f)
        {
            // SNDLVL_NONE: heard at the same volume wherever it is
            return 0f;
        }

        return MathF.Pow(10f, (ReferenceDecibels - db) / 20f) / ReferenceDistance;
    }

    private static float? ParseSoundLevel(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var suffix = token.StartsWith("SNDLVL_", StringComparison.OrdinalIgnoreCase)
            ? token["SNDLVL_".Length..]
            : token;

        if (suffix.EndsWith("db", StringComparison.OrdinalIgnoreCase)
            && float.TryParse(suffix[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDb))
        {
            return parsedDb;
        }

        if (NamedSoundLevels.TryGetValue(suffix, out var named))
        {
            return named;
        }

        return float.TryParse(suffix, NumberStyles.Float, CultureInfo.InvariantCulture, out var bare) ? bare : null;
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

    // The engine's reference pair (its snd_refdb/snd_refdist): 60 dB reproduced at full volume three feet out.
    private const float ReferenceDecibels = 60f;
    private const float ReferenceDistance = 36f;
}
