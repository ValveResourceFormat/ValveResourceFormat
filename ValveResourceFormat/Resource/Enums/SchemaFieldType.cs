namespace ValveResourceFormat
{
    /// <summary>
    /// Schema field data types.
    /// </summary>
    public enum SchemaFieldType
    {
#pragma warning disable CS1591
        Unknown = 0,
        Struct = 1,
        Enum = 2,
        ExternalReference = 3,
        Char = 4,
        UChar = 5,
        Int = 6,
        UInt = 7,
        Float_8 = 8,
        Double = 9,
        SByte = 10, // Int8
        Byte = 11, // UInt8
        Int16 = 12,
        UInt16 = 13,
        Int32 = 14,
        UInt32 = 15,
        Int64 = 16,
        UInt64 = 17,
        Float = 18, // Float32
        Float64 = 19,
        Time = 20,
        Vector2D = 21,
        Vector3D = 22,
        Vector4D = 23,
        QAngle = 24,
        Quaternion = 25,
        VMatrix = 26,
        Fltx4 = 27,
        Color = 28,
        UniqueId = 29,
        Boolean = 30,
        ResourceString = 31,
        Void = 32,
        Matrix3x4 = 33,
        UtlSymbol = 34,
        UtlString = 35,
        Matrix3x4a = 36,
        UtlBinaryBlock = 37,
        Uuid = 38,
        OpaqueType = 39,
        Transform = 40,
        Unused = 41,
        RadianEuler = 42,
        DegreeEuler = 43,
        FourVectors = 44,
#pragma warning restore CS1591
    }
}
