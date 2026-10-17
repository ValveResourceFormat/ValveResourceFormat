using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>Reads keys of environment entities the way the editor does.</summary>
internal static class EnvironmentKeys
{
    /// <summary>Reads the integer a value starts with, or a default where the value is empty.</summary>
    /// <param name="value">The value.</param>
    /// <param name="defaultValue">The integer where the value is empty.</param>
    /// <returns>The integer.</returns>
    public static int ParseInteger(string value, int defaultValue) => value.Length > 0 ? LeadingNumber.ParseInteger(value) : defaultValue;

    /// <summary>Reads a color of three integers from 0 to 255, each cut down to a byte, missing ones zero.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The color, opaque.</returns>
    public static Color32 ParseColor(string value)
    {
        Span<byte> channels = stackalloc byte[3];
        var index = 0;

        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (index == channels.Length)
            {
                break;
            }

            channels[index++] = unchecked((byte)LeadingNumber.ParseInteger(part));
        }

        return new Color32(channels[0], channels[1], channels[2]);
    }
}
