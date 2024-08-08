namespace ValveResourceFormat.CompiledShader
{
    [Flags]
    public enum LeadFlags
    {
        None = 0x00,
        Attribute = 0x01,
        Dynamic = 0x02,
        Expression = 0x04,
        DynMaterial = 0x08,
    }
}
