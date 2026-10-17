using System.Globalization;

namespace ValveResourceFormat.Utils;

/// <summary>
/// Reads the number text starts with the way C's <c>atoi</c> and <c>atof</c> do: whatever follows it is
/// ignored, and text that starts with no number is zero.
/// </summary>
public static class LeadingNumber
{
    /// <summary>Reads the integer text starts with: an optional sign and the digits after it.</summary>
    /// <param name="text">The text, which may start with white space.</param>
    /// <returns>The integer, or zero when the text starts with none or it does not fit.</returns>
    public static int ParseInteger(ReadOnlySpan<char> text)
    {
        text = text.TrimStart();

        var length = SkipSign(text);
        length = SkipDigits(text, length);

        return int.TryParse(text[..length], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    /// <summary>Reads the number text starts with: an optional sign, digits, and a fraction after a point.</summary>
    /// <param name="text">The text, which may start with white space.</param>
    /// <returns>The number, or zero when the text starts with none.</returns>
    public static float ParseFloat(ReadOnlySpan<char> text)
    {
        text = text.TrimStart();

        var length = SkipSign(text);
        length = SkipDigits(text, length);

        if (length < text.Length && text[length] == '.')
        {
            length = SkipDigits(text, length + 1);
        }

        return float.TryParse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;
    }

    private static int SkipSign(ReadOnlySpan<char> text) => text.Length > 0 && text[0] is '+' or '-' ? 1 : 0;

    private static int SkipDigits(ReadOnlySpan<char> text, int start)
    {
        while (start < text.Length && char.IsAsciiDigit(text[start]))
        {
            start++;
        }

        return start;
    }
}
