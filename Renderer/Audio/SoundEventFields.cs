using System.Globalization;
using ValveKeyValue;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Reads sound event fields. Typed values are used as they are, while string values
/// are split on brackets, commas and spaces and each token is parsed leniently, so authoring leftovers such as
/// a "1.0,1.0" range do not fail the whole event.
/// </summary>
internal static class SoundEventFields
{
    private const string Separators = "[], ";

    /// <summary>Gets a float field. A string holding several values yields the first one.</summary>
    public static float GetSoundFloat(this KVObject data, string name, float defaultValue = 0f)
    {
        if (!data.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (value.ValueType == KVValueType.String)
        {
            Span<float> result = stackalloc float[1];
            ParseFloats((string)value, result);
            return result[0];
        }

        if (value.IsArray)
        {
            // An array whose element count does not match the field is ignored
            return value.Count == 1 ? ToFloat(value[0]) : defaultValue;
        }

        return (float)value;
    }

    /// <summary>
    /// Gets a boolean field. Strings are true for "true", "1" or anything containing "1.0";
    /// other values are true when positive.
    /// </summary>
    public static bool GetSoundBool(this KVObject data, string name, bool defaultValue = false)
    {
        if (!data.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (value.ValueType == KVValueType.String)
        {
            var text = (string)value;

            return text.Equals("true", StringComparison.OrdinalIgnoreCase)
                || text == "1"
                || text.Contains("1.0", StringComparison.Ordinal);
        }

        if (value.IsArray)
        {
            return defaultValue;
        }

        return (float)value > 0f;
    }

    /// <summary>Gets a three component vector field, zero when absent or when an array has the wrong element count.</summary>
    public static Vector3 GetSoundVector3(this KVObject data, string name)
    {
        if (!data.TryGetValue(name, out var value))
        {
            return Vector3.Zero;
        }

        Span<float> result = stackalloc float[3];

        if (value.ValueType == KVValueType.String)
        {
            ParseFloats((string)value, result);
            return new Vector3(result);
        }

        if (!value.IsArray || value.Count != 3)
        {
            return Vector3.Zero;
        }

        return new Vector3(ToFloat(value[0]), ToFloat(value[1]), ToFloat(value[2]));
    }

    private static float ToFloat(KVObject value)
    {
        if (value.ValueType == KVValueType.String)
        {
            Span<float> result = stackalloc float[1];
            ParseFloats((string)value, result);
            return result[0];
        }

        return (float)value;
    }

    /// <summary>
    /// Fills every component from the tokens in order. Components past the last token repeat it,
    /// surplus tokens are ignored, and no tokens at all yields zeroes.
    /// </summary>
    private static void ParseFloats(ReadOnlySpan<char> text, Span<float> destination)
    {
        var count = 0;

        foreach (var range in text.SplitAny(Separators))
        {
            var token = text[range];

            if (token.IsEmpty)
            {
                continue;
            }

            destination[count++] = ParseLeadingFloat(token);

            if (count == destination.Length)
            {
                return;
            }
        }

        var last = count > 0 ? destination[count - 1] : 0f;
        destination[count..].Fill(last);
    }

    private static float ParseLeadingFloat(ReadOnlySpan<char> token)
    {
        // Like atof: use the longest numeric prefix, and treat a token without one as zero
        for (var length = token.Length; length > 0; length--)
        {
            if (float.TryParse(token[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }

        return 0f;
    }
}
