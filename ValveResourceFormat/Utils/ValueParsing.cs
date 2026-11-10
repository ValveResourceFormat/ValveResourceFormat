using System.Globalization;

namespace ValveResourceFormat.Utils;

/// <summary>
/// Converts text to numbers, booleans, vectors and colors with the rules the engine applies to keyvalue strings.
/// The <c>Parse</c> and <c>TryParse</c> methods need the whole text to be a value, the <c>ParseLeading</c> methods read the number it starts with.
/// Failures never throw: the <c>Parse</c> methods return a default and the <c>TryParse</c> methods return false.
/// </summary>
public static class ValueParsing
{
    private const NumberStyles IntegerStyles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign;
    private const NumberStyles FloatStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    /// <summary>
    /// Parses a whole string as a float. Surrounding white space and one leading plus sign are skipped; the rest must be
    /// a number, or an infinity or NaN spelled "inf", "infinity" or "nan" in any case. Other trailing characters,
    /// thousands separators and hex are failures. A value out of range is infinity or zero.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">The parsed value, or zero on failure.</param>
    /// <returns>Whether the whole text was a float.</returns>
    /// <remarks>
    /// Trailing text fails, unlike the leading-number methods.
    /// </remarks>
    public static bool TryParseFloat(ReadOnlySpan<char> text, out float value)
        => TryParseFloatingPoint(text, out value);

    /// <summary>
    /// Parses a whole string as a double with the same rules as <see cref="TryParseFloat"/>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">The parsed value, or zero on failure.</param>
    /// <returns>Whether the whole text was a number.</returns>
    public static bool TryParseDouble(ReadOnlySpan<char> text, out double value)
        => TryParseFloatingPoint(text, out value);

    /// <summary>
    /// Parses a float like <see cref="TryParseFloat"/>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="defaultValue">The value returned when the text is not a float.</param>
    /// <returns>The parsed value, or <paramref name="defaultValue"/>.</returns>
    public static float ParseFloat(ReadOnlySpan<char> text, float defaultValue = 0f)
        => TryParseFloat(text, out var value) ? value : defaultValue;

    /// <summary>
    /// Parses a whole string as a 32-bit integer: optional leading white space, an optional sign and base-10 digits.
    /// A fraction, trailing characters and values outside the int range are failures.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">The parsed value, or zero on failure.</param>
    /// <returns>Whether the whole text was an integer.</returns>
    public static bool TryParseInt(ReadOnlySpan<char> text, out int value)
        => int.TryParse(text, IntegerStyles, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Parses a boolean: "true", "1" and "yes" are true, "false", "0" and "no" are false, in any case.
    /// Any other text that is wholly a base-10 integer or a float is true unless it is zero, and empty text is false.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="defaultValue">The value returned for any other text.</param>
    /// <returns>The parsed value, or <paramref name="defaultValue"/>.</returns>
    /// <remarks>
    /// Trailing text fails, unlike the leading-number methods, so "1abc" gives <paramref name="defaultValue"/>.
    /// </remarks>
    public static bool ParseBool(ReadOnlySpan<char> text, bool defaultValue = false)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        if (text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Equals("false", StringComparison.OrdinalIgnoreCase)
            || text.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (long.TryParse(text, IntegerStyles, CultureInfo.InvariantCulture, out var number))
        {
            return number != 0;
        }

        if (TryParseFloat(text, out var floatNumber))
        {
            return floatNumber != 0f;
        }

        return defaultValue;
    }

    /// <summary>
    /// Parses a sound event boolean: "true" and "1" in any case, or any text containing "1.0", are true.
    /// All other text is false.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed value.</returns>
    /// <remarks>
    /// "1.0" matches anywhere in the text, so "11.05" is true.
    /// </remarks>
    public static bool ParseSoundEventBool(ReadOnlySpan<char> text)
        => text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("1", StringComparison.Ordinal)
            || text.Contains("1.0", StringComparison.Ordinal);

    /// <summary>
    /// Parses space separated floats into <paramref name="destination"/>. Empty parts are dropped, a part that is not
    /// a float reads as zero, missing components are zero and extra parts are ignored.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="destination">Receives the components.</param>
    /// <returns>How many parts were read, at most the length of <paramref name="destination"/>.</returns>
    /// <remarks>
    /// Zero-filling is the rule for keyfields. Some other engine readers keep the whole default when the count is wrong.
    /// </remarks>
    public static int ParseVector(ReadOnlySpan<char> text, Span<float> destination)
    {
        destination.Clear();

        var count = 0;

        foreach (var range in text.Split(' '))
        {
            if (count == destination.Length)
            {
                break;
            }

            var token = text[range];

            if (token.IsEmpty)
            {
                continue;
            }

            destination[count] = ParseFloat(token);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Reads numbers one after another into <paramref name="destination"/>. White space before each number is
    /// skipped and no separator is needed, so "1-2" reads as 1 and -2. Each number is the longest prefix that
    /// parses as a float, and reading stops at the first position that is not a number.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="destination">Receives the numbers; components past the count read are zero.</param>
    /// <returns>How many numbers were read.</returns>
    /// <remarks>
    /// For values the engine reads as numbers one after another, such as angles and particle control points.
    /// </remarks>
    public static int ParseFloatArray(ReadOnlySpan<char> text, Span<float> destination)
    {
        destination.Clear();

        var count = 0;

        while (count < destination.Length)
        {
            text = text.TrimStart();

            if (!float.TryParsePartial(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value, out var consumed))
            {
                break;
            }

            destination[count] = value;
            count++;
            text = text[consumed..];
        }

        return count;
    }

    /// <summary>
    /// Reads exactly three numbers like <see cref="ParseFloatArray"/>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">The vector, or zero when the text does not hold three numbers.</param>
    /// <returns>Whether three numbers were read.</returns>
    public static bool TryParseVector3(ReadOnlySpan<char> text, out Vector3 value)
    {
        Span<float> components = stackalloc float[3];

        if (ParseFloatArray(text, components) != components.Length)
        {
            value = default;
            return false;
        }

        value = new Vector3(components);
        return true;
    }

    /// <summary>
    /// Reads the two points of an axis, written as "x y z, x y z", into six components. The numbers are read like
    /// <see cref="ParseFloatArray"/>, and the end point is read only when the start point has three numbers and a comma follows it.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="destination">Receives the start point and then the end point, six components; the ones not read are zero.</param>
    /// <returns>How many numbers were read.</returns>
    public static int ParseAxis(ReadOnlySpan<char> text, Span<float> destination)
    {
        destination.Clear();

        var comma = text.IndexOf(',');
        var startCount = ParseFloatArray(comma < 0 ? text : text[..comma], destination[..3]);

        if (startCount < 3 || comma < 0)
        {
            return startCount;
        }

        return startCount + ParseFloatArray(text[(comma + 1)..], destination[3..6]);
    }

    /// <summary>
    /// Parses a space separated color. Four parts are red, green, blue and alpha, and three are red, green and blue
    /// with full alpha. Each part is read with <see cref="ParseLeadingInt"/>, so "0.5" is 0, and wraps to a byte, so 256 is 0 and -1 is 255.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="color">The color, or the default color when the text has any other number of parts.</param>
    /// <param name="clamp">Whether parts are clamped to 0-255 instead of wrapping.</param>
    /// <returns>Whether the text has three or four parts.</returns>
    /// <remarks>
    /// Parts wrap to a byte as the engine's color storage does. Other part counts are left to the caller, whose default differs per key.
    /// </remarks>
    public static bool TryParseColor(ReadOnlySpan<char> text, out Color32 color, bool clamp = false)
    {
        Span<int> parts = stackalloc int[4];
        var count = 0;

        foreach (var range in text.Split(' '))
        {
            var token = text[range];

            if (token.IsEmpty)
            {
                continue;
            }

            if (count < parts.Length)
            {
                parts[count] = ParseLeadingInt(token);
            }

            count++;
        }

        if (count is not (3 or 4))
        {
            color = default;
            return false;
        }

        var alpha = count == 4 ? ToChannel(parts[3], clamp) : byte.MaxValue;
        color = new Color32(ToChannel(parts[0], clamp), ToChannel(parts[1], clamp), ToChannel(parts[2], clamp), alpha);
        return true;
    }

    private static byte ToChannel(int part, bool clamp)
        => clamp ? (byte)Math.Clamp(part, 0, byte.MaxValue) : unchecked((byte)part);

    /// <summary>Reads the integer text starts with like <see cref="TryParseLeadingInt"/>: an optional sign and the digits after it.</summary>
    /// <param name="text">The text, which may start with white space.</param>
    /// <returns>The integer, or zero when the text starts with none or it does not fit.</returns>
    public static int ParseLeadingInt(ReadOnlySpan<char> text)
        => TryParseLeadingInt(text, out var value) ? value : 0;

    /// <summary>
    /// Reads the integer text starts with the way the engine's keyvalue getters do: base-10 digits after optional
    /// white space and a sign, ignoring whatever follows, so "5abc" is 5 and "1.5" is 1.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">The integer, or zero when there are no digits or it does not fit.</param>
    /// <returns>Whether the text starts with an integer.</returns>
    /// <remarks>
    /// Like the engine's keyvalue getters, "0x10" reads 0 and a 32-bit overflow fails. Text read with C's <c>atoi</c> differs,
    /// which reads hex and character literals, wraps on overflow and reads zero after a leading newline, but real data never contains these.
    /// Integer fields an entity class declares need the whole text to be an integer, but no shipped map has text where the two rules differ.
    /// </remarks>
    private static bool TryParseLeadingInt(ReadOnlySpan<char> text, out int value)
    {
        value = 0;

        if (!TryParseLeadingLong(text, out var number) || number is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        value = (int)number;
        return true;
    }

    /// <summary>
    /// Reads the 64-bit integer text starts with, with the same rules as <see cref="TryParseLeadingInt"/>.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">The integer, or zero when there are no digits or it does not fit.</param>
    /// <returns>Whether the text starts with an integer.</returns>
    /// <remarks>
    /// An overflow fails where the engine clamps to the 64-bit range.
    /// </remarks>
    public static bool TryParseLeadingLong(ReadOnlySpan<char> text, out long value)
        => long.TryParsePartial(text, IntegerStyles, CultureInfo.InvariantCulture, out value, out _);

    /// <summary>Reads the number text starts with the way C's <c>atof</c> does: an optional sign, digits, a fraction after a point, and an exponent.</summary>
    /// <param name="text">The text, which may start with white space.</param>
    /// <returns>The number, or zero when the text starts with none.</returns>
    /// <remarks>
    /// Real data never contains the cases where this differs from the engine: "inf" and "nan" are read where the engine
    /// reads 0, and a second decimal point ends the number where the engine skips the earlier ones, so "1.2.3" is 1.2 and not 12.3.
    /// </remarks>
    public static float ParseLeadingFloat(ReadOnlySpan<char> text)
        => float.TryParsePartial(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value, out _) ? value : 0f;

    /// <summary>Reads the leading number of each token of text, where tokens are separated by any of the separator characters and empty tokens are skipped.</summary>
    /// <param name="text">The text to split.</param>
    /// <param name="separators">The characters that separate tokens.</param>
    /// <param name="destination">Receives the numbers in order. Entries past the last token are zero.</param>
    /// <returns>How many tokens were read, at most the length of <paramref name="destination"/>.</returns>
    /// <remarks>Each token is read with <see cref="ParseLeadingFloat"/>, so a token without a number is zero.</remarks>
    public static int ParseLeadingFloats(ReadOnlySpan<char> text, ReadOnlySpan<char> separators, Span<float> destination)
    {
        destination.Clear();

        var count = 0;

        foreach (var range in text.SplitAny(separators))
        {
            if (count == destination.Length)
            {
                break;
            }

            var token = text[range];

            if (token.IsEmpty)
            {
                continue;
            }

            destination[count++] = ParseLeadingFloat(token);
        }

        return count;
    }

    private static bool TryParseFloatingPoint<T>(ReadOnlySpan<char> text, out T value) where T : IFloatingPointIeee754<T>
    {
        value = T.Zero;

        text = text.Trim();

        if (!text.IsEmpty && text[0] == '+')
        {
            text = text[1..];
        }

        if (text.IsEmpty)
        {
            return false;
        }

        if (T.TryParse(text, FloatStyles, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        var negative = text[0] == '-';
        var word = negative ? text[1..] : text;

        if (word.Equals("inf", StringComparison.OrdinalIgnoreCase)
            || word.Equals("infinity", StringComparison.OrdinalIgnoreCase))
        {
            value = negative ? T.NegativeInfinity : T.PositiveInfinity;
            return true;
        }

        if (word.Equals("nan", StringComparison.OrdinalIgnoreCase))
        {
            value = T.NaN;
            return true;
        }

        return false;
    }
}
