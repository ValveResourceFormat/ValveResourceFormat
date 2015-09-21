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
        Ushort = 13,
        Number = 14,
        Flags = 15,
        Int64 = 16,
        Uint64 = 17,
        Float = 18,
        Vector3 = 22,
        Vector4 = 23,
        Quaternion = 25,
        Color = 28,
        Boolean = 30,
        String = 31,
    }
}
