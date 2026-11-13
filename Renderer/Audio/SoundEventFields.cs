using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Reads sound event fields. Typed values are used as they are, while string values of float and vector fields
/// are split on brackets, commas and spaces and each token is parsed leniently, so authoring leftovers such as
/// a "1.0,1.0" range do not fail the whole event.
/// </summary>
internal static class SoundEventFields
{
    private const string Separators = "[], ";

    /// <summary>Gets a float field. A string holding several values yields the first one.</summary>
    /// <remarks>Not a KVObjectExtensions method: text is a bracket or comma list read as leading numbers.</remarks>
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

        return value.ToFloatOrDefault(defaultValue);
    }

    /// <summary>
    /// Gets a boolean field. Text follows <see cref="ValueParsing.ParseSoundEventBool"/> and numbers are true when not zero.
    /// An array of one element uses that element, where text is parsed whole as a number. Any other array yields the default.
    /// </summary>
    /// <remarks>Not a KVObjectExtensions method: it uses its own text and one-element array rules.</remarks>
    public static bool GetSoundBool(this KVObject data, string name, bool defaultValue = false)
    {
        if (!data.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (value.IsArray)
        {
            if (value.Count != 1)
            {
                return defaultValue;
            }

            var element = value[0];

            if (element.ValueType == KVValueType.String)
            {
                return ValueParsing.TryParseFloat((string)element, out var number) && number != 0f;
            }

            return element.ToFloatOrDefault() != 0f;
        }

        if (value.ValueType == KVValueType.String)
        {
            return ValueParsing.ParseSoundEventBool((string)value);
        }

        return value.ToFloatOrDefault() != 0f;
    }

    /// <summary>Gets a three component vector field, zero when absent or when an array has the wrong element count.</summary>
    /// <remarks>Not a KVObjectExtensions method: text is a bracket or comma list, and a wrong-sized array is ignored.</remarks>
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

    // Not a KVObjectExtensions method: text goes through the lenient list parser instead of a whole-number parse.
    private static float ToFloat(KVObject value)
    {
        if (value.ValueType == KVValueType.String)
        {
            Span<float> result = stackalloc float[1];
            ParseFloats((string)value, result);
            return result[0];
        }

        return value.ToFloatOrDefault();
    }

    /// <summary>
    /// Fills every component from the tokens in order. Components past the last token repeat it,
    /// surplus tokens are ignored, and no tokens at all yields zeroes.
    /// </summary>
    /// <remarks>Not a ValueParsing method: the last value repeats into the remaining components.</remarks>
    private static void ParseFloats(ReadOnlySpan<char> text, Span<float> destination)
    {
        var count = ValueParsing.ParseLeadingFloats(text, Separators, destination);

        if (count == 0)
        {
            return;
        }

        destination[count..].Fill(destination[count - 1]);
    }
}
