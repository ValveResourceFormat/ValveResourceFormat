namespace ValveResourceFormat
{
    public enum SchemaIndirectionType
    {
#pragma warning disable CS1591
        Unknown = 0,
        Pointer = 1,
        Reference = 2,
        ResourcePointer = 3,
        ResourceArray = 4,
        UtlVector = 5,
        UtlReference = 6,
        Ignorable = 7,
        Opaque = 8,
#pragma warning restore CS1591
    }
}
