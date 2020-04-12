using System;

namespace ValveResourceFormat
{
#pragma warning disable CA1717 // it thinks its plural
    public enum VTexExtraData
    {
        UNKNOWN = 0,
        FALLBACK_BITS = 1,
        SHEET = 2,
        FILL_TO_POWER_OF_TWO = 3,
        COMPRESSED_MIP_SIZE = 4,
    }
#pragma warning restore CA1717
}
