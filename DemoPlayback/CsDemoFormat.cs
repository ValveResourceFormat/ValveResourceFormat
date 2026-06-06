using System;
using System.IO;

namespace ValveResourceFormat.DemoPlayback;

public static class CsDemoFormat
{
    private static ReadOnlySpan<byte> Magic => "PBDEMS2\0"u8;

    public static int MagicLength => Magic.Length;

    public static bool IsAccepted(ReadOnlySpan<byte> header, string? fileName = null)
    {
        if (header.Length >= Magic.Length && header[..Magic.Length].SequenceEqual(Magic))
        {
            return true;
        }

        return fileName != null && Path.GetExtension(fileName).Equals(".dem", StringComparison.OrdinalIgnoreCase);
    }
}
