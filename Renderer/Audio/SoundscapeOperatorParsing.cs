using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A "soundlevel" or "attenuation" setting of a soundscape operator. Both write the same level, so the one
/// that comes last in the block wins.
/// </summary>
/// <param name="Min">The low end as authored: a level, or an attenuation value when <paramref name="IsAttenuation"/> is set.</param>
/// <param name="Max">The high end as authored.</param>
/// <param name="IsAttenuation">Whether the ends are attenuation values that convert to a level.</param>
internal readonly record struct SoundLevelRange(float Min, float Max, bool IsAttenuation)
{
    /// <summary>The level used when an operator authors none.</summary>
    public static SoundLevelRange Default { get; } = new(75f, 75f, false);

    /// <summary>
    /// Gets a level for an operator that picks once: a value is picked in the range first and then converted
    /// to a level, which a sound level truncates to a whole number.
    /// </summary>
    /// <param name="amount">The position in the range, from 0 to 1.</param>
    public float Pick(float amount)
    {
        var value = MathUtils.Lerp(Min, Max, amount);

        return IsAttenuation ? SoundscapeOperatorParsing.AttenuationToLevel(value) : (int)value;
    }

    /// <summary>
    /// Gets a level for an operator that rolls on every play: both ends are converted to levels and the roll
    /// is between them.
    /// </summary>
    /// <param name="amount">The position in the range, from 0 to 1.</param>
    public float Roll(float amount)
    {
        if (!IsAttenuation)
        {
            return MathUtils.Lerp(Min, Max, amount);
        }

        var low = SoundscapeOperatorParsing.AttenuationToLevel(Min);
        var high = SoundscapeOperatorParsing.AttenuationToLevel(Max);

        return MathUtils.Lerp(low, high, amount);
    }
}

/// <summary>
/// Shared reading of Source 1 soundscape script operators ("playrandom", "playlooping"): the old KeyValues1
/// format authors ranges as a single "min, max" string and named sound levels ("SNDLVL_140db").
/// </summary>
/// <remarks>
/// Not in ValueParsing: sound level names, the attenuation conversion and the range rules are soundscape
/// semantics rather than general text parsing.
/// </remarks>
internal static class SoundscapeOperatorParsing
{
    /// <summary>
    /// Reads a "min, max" (or a single "value") text into a range. Text that is null, empty or white space gives
    /// <paramref name="defaultValue"/> for both ends. Each comma separated part is read as the number it
    /// starts with, empty parts are skipped, and a range with one part has it as both ends.
    /// </summary>
    /// <remarks>Not a ValueParsing method: the single part serving as both ends is a soundscape script rule.</remarks>
    public static (float Min, float Max) ReadRange(string? text, float defaultValue)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (defaultValue, defaultValue);
        }

        Span<float> values = stackalloc float[2];
        var count = ValueParsing.ParseLeadingFloats(text, ",", values);

        return (values[0], count > 1 ? values[1] : values[0]);
    }

    /// <summary>
    /// Reads the "origin" ("x, y, z") of an operator as a position, or null when it has none. Each comma separated
    /// part is read as the number it starts with, and missing parts are zero.
    /// </summary>
    /// <remarks>Not a ValueParsing method: the key and its comma separated form are soundscape script rules.</remarks>
    public static Vector3? ReadOrigin(KVObject data)
    {
        var text = data.GetStringProperty("origin");

        if (text == null)
        {
            return null;
        }

        Span<float> components = stackalloc float[3];
        ValueParsing.ParseLeadingFloats(text, ",", components);

        return new Vector3(components);
    }

    /// <summary>
    /// Reads the level of an operator from its "soundlevel" and "attenuation" keys, the last of them in the block
    /// winning. A "soundlevel" starting with "SNDLVL_" is a named level or a number after the prefix, and any
    /// other text is a range like "volume".
    /// </summary>
    public static SoundLevelRange ReadSoundLevel(KVObject data)
    {
        var level = SoundLevelRange.Default;

        foreach (var entry in data)
        {
            if (entry.Key.Equals("soundlevel", StringComparison.Ordinal))
            {
                var text = (string)entry.Value;

                if (text.StartsWith(SoundLevelPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var named = ReadNamedSoundLevel(text);
                    level = new SoundLevelRange(named, named, false);
                }
                else
                {
                    var (min, max) = ReadRange(text, DefaultSoundLevel);
                    level = new SoundLevelRange(min, max, false);
                }
            }
            else if (entry.Key.Equals("attenuation", StringComparison.Ordinal))
            {
                var (min, max) = ReadRange((string)entry.Value, 0f);
                level = new SoundLevelRange(min, max, true);
            }
        }

        return level;
    }

    /// <summary>Converts an attenuation value to a level. Zero is no attenuation.</summary>
    public static float AttenuationToLevel(float attenuation)
    {
        if (attenuation == 0f)
        {
            return 0f;
        }

        return (int)(20.0 / attenuation + 50.0);
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
    /// Converts a sound level in decibels into the distance multiplier: the reciprocal of the distance the
    /// sound stays at full volume out to, past which it follows the inverse distance law (see
    /// <see cref="SampleProviders.SampleProvider3D.DistanceMult"/>). Zero means it never attenuates
    /// (SNDLVL_NONE).
    /// </summary>
    /// <remarks>
    /// A sound level is the sound pressure the source is authored at, so the model is the physical one:
    /// a source at <see cref="ReferenceDecibels"/> is played at full volume out to
    /// <see cref="ReferenceDistance"/> units, and every doubling of distance past that halves the
    /// amplitude. It never reaches zero - which is the point of it, a loud sound stays faintly audible far
    /// past where a "range" would have cut it off entirely.
    /// </remarks>
    public static float SoundLevelToDistanceMult(float db)
    {
        if (db <= 0)
        {
            // SNDLVL_NONE: heard at the same volume wherever it is
            return 0f;
        }

        return MathUtils.DecibelsToLinear(ReferenceDecibels - db) / ReferenceDistance;
    }

    private static int ReadNamedSoundLevel(string token)
    {
        if (NamedSoundLevels.TryGetValue(token, out var named))
        {
            return named;
        }

        var level = ValueParsing.ParseLeadingInt(token.AsSpan(SoundLevelPrefix.Length));

        if (level is >= 1 and <= 180)
        {
            return level;
        }

        if (token.Contains("db", StringComparison.Ordinal))
        {
            return ValueParsing.ParseLeadingInt(token);
        }

        var number = ValueParsing.ParseLeadingFloat(token);

        return number >= 20f ? (int)number : DefaultSoundLevel;
    }

    private const string SoundLevelPrefix = "SNDLVL_";
    private const int DefaultSoundLevel = 75;

    // The names with a number in them, such as SNDLVL_85dB, are read by the prefix rule
    private static readonly Dictionary<string, int> NamedSoundLevels = new(StringComparer.Ordinal)
    {
        ["SNDLVL_NONE"] = 0,
        ["SNDLVL_IDLE"] = 60,
        ["SNDLVL_STATIC"] = 66,
        ["SNDLVL_NORM"] = 75,
        ["SNDLVL_TALKING"] = 80,
        ["SNDLVL_SINGING"] = 80,
        ["SNDLVL_GUNFIRE"] = 140,
        ["SNDLVL_WEAPON"] = 150,
    };

    // Reference pair: 60 dB reproduced at full volume three feet out.
    private const float ReferenceDecibels = 60f;
    private const float ReferenceDistance = 36f;
}
