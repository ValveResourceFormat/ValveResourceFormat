using System.Globalization;

namespace GUI.Utils;

internal static class HumanReadableByteSizeFormatter
{
    // https://stackoverflow.com/a/11124118
    public static string Format(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        string suffix;
        double readable;

        switch (value)
        {
            case >= 0x1000000000000000:
                suffix = "EiB";
                readable = value >> 50;
                break;
            case >= 0x4000000000000:
                suffix = "PiB";
                readable = value >> 40;
                break;
            case >= 0x10000000000:
                suffix = "TiB";
                readable = value >> 30;
                break;
            case >= 0x40000000:
                suffix = "GiB";
                readable = value >> 20;
                break;
            case >= 0x100000:
                suffix = "MiB";
                readable = value >> 10;
                break;
            case >= 0x400:
                suffix = "KiB";
                readable = value;
                break;
            default:
                return value.ToString("0 B", CultureInfo.InvariantCulture);
        }

        return (readable / 1024).ToString("0.## ", CultureInfo.InvariantCulture) + suffix;

        /*
        if (value == 0)
        {
            return $"0 {suffixes[0]}";
        }

        var position = (int)Math.Floor(Math.Log(Math.Abs(value), 1024));
        var renderedValue = value / Math.Pow(1024, position);
        return $"{renderedValue:0.#} {suffixes[position]}";
        */
    }
}
