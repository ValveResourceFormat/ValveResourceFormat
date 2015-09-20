using System;

namespace ValveResourceFormat
{
    public enum DataType : short
    {
        SubStructure = 1,
        Enum = 2,
        Extref = 3,
        String4 = 4,
        Byte = 11,
        Sint = 12,
        Number = 14,
        Flags = 15,
        Float = 18,
        Vector3 = 22,
        Massive = 23,
        Vector4 = 28,
        Boolean = 30,
        String = 31,
    }
}
