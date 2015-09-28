namespace ValveResourceFormat
{
    public enum DataType
    {
        Struct = 1,
        Enum = 2, // TODO: not verified with resourceinfo
        ExternalReference = 3,
        String4 = 4, // TODO: not verified with resourceinfo
        Byte = 11, // int8
        Int16 = 12,
        UInt16 = 13,
        Int32 = 14,
        UInt32 = 15,
        Int64 = 16, // TODO: not verified with resourceinfo
        UInt64 = 17,
        Float = 18,
        Vector = 22,
        Vector4 = 23, // TODO: not verified with resourceinfo
        Quaternion = 25,
        Fltx4 = 27,
        Color = 28, // TODO: not verified with resourceinfo
        Boolean = 30,
        String = 31, // CResourceString
        Matrix3x4 = 33,
        Matrix3x4a = 36,
        CTransform = 40,
    }
}
