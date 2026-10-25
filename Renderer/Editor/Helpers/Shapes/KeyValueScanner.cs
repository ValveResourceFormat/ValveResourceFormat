using System.Globalization;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// Reads the numbers of a key value the way a C format string of <c>%f</c> conversions reads them: each number
/// is the longest prefix of what follows the whitespace before it that reads as one, and reading stops at the
/// first that is missing or is not a number, with the values read so far kept.
/// </summary>
internal static class KeyValueScanner
{
    /// <summary>Reads numbers into values, in order, writing each value as its number is read.</summary>
    /// <param name="text">The key value.</param>
    /// <param name="values">Where the numbers go; the ones past the last number read keep what they held.</param>
    /// <param name="commaBefore">
    /// Index of the number that must come right after a comma, with no whitespace before it, or -1 for none.
    /// </param>
    /// <returns>How many numbers were read.</returns>
    public static int Scan(ReadOnlySpan<char> text, Span<float> values, int commaBefore = -1)
    {
        var position = 0;

        for (var i = 0; i < values.Length; i++)
        {
            if (i == commaBefore)
            {
                if (position >= text.Length || text[position] != ',')
                {
                    return i;
                }

                position++;
            }

            if (!TryReadNumber(text, ref position, out var value))
            {
                return i;
            }

            values[i] = value;
        }

        return values.Length;
    }

    /// <summary>Reads a point written as <c>x y z</c>.</summary>
    /// <param name="text">The key value.</param>
    /// <param name="point">The point, when all three numbers are read.</param>
    /// <returns>Whether all three numbers were read.</returns>
    public static bool TryReadVector(ReadOnlySpan<char> text, out Vector3 point)
    {
        Span<float> components = stackalloc float[3];

        var isRead = Scan(text, components) == components.Length;
        point = isRead ? new Vector3(components) : default;

        return isRead;
    }

    /// <summary>
    /// Reads the two points of an axis, written as <c>x y z, x y z</c>, into the components of both. The
    /// components past the last number read keep the values they had.
    /// </summary>
    /// <param name="text">The key value.</param>
    /// <param name="start">The first point.</param>
    /// <param name="end">The second point.</param>
    /// <returns>How many of the six components were read.</returns>
    public static int ReadAxis(ReadOnlySpan<char> text, ref Vector3 start, ref Vector3 end)
    {
        Span<float> components = [start.X, start.Y, start.Z, end.X, end.Y, end.Z];

        var count = Scan(text, components, commaBefore: 3);

        start = new Vector3(components[..3]);
        end = new Vector3(components[3..]);

        return count;
    }

    // A sign, digits with an optional fraction, and an exponent that is only taken when it has digits
    private static bool TryReadNumber(ReadOnlySpan<char> text, ref int position, out float value)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        var start = position;
        var end = position;

        if (end < text.Length && text[end] is '+' or '-')
        {
            end++;
        }

        var digits = CountDigits(text, end);
        end += digits;

        if (end < text.Length && text[end] == '.')
        {
            var fractionDigits = CountDigits(text, end + 1);
            end += 1 + fractionDigits;
            digits += fractionDigits;
        }

        if (digits == 0)
        {
            value = 0f;
            return false;
        }

        if (end < text.Length && text[end] is 'e' or 'E')
        {
            var exponent = end + 1;

            if (exponent < text.Length && text[exponent] is '+' or '-')
            {
                exponent++;
            }

            var exponentDigits = CountDigits(text, exponent);

            if (exponentDigits > 0)
            {
                end = exponent + exponentDigits;
            }
        }

        position = end;
        value = float.Parse(text[start..end], NumberStyles.Float, CultureInfo.InvariantCulture);

        return true;
    }

    private static int CountDigits(ReadOnlySpan<char> text, int start)
    {
        var count = 0;

        while (start + count < text.Length && char.IsAsciiDigit(text[start + count]))
        {
            count++;
        }

        return count;
    }
}
