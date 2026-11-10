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
        const NumberStyles Styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign;

        return int.TryParsePartial(text, Styles, CultureInfo.InvariantCulture, out var value, out _) ? value : 0;
    }

    /// <summary>Reads the number text starts with: an optional sign, digits, a fraction after a point, and an exponent.</summary>
    /// <param name="text">The text, which may start with white space.</param>
    /// <returns>The number, or zero when the text starts with none.</returns>
    public static float ParseFloat(ReadOnlySpan<char> text)
        => float.TryParsePartial(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value, out _) ? value : 0f;
}
