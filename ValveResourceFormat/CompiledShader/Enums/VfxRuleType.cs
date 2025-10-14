namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Shader rule types for combo constraints.
/// </summary>
public enum VfxRuleType : byte
{
#pragma warning disable CS1591
    Unknown = 0,
    Feature = 1,
    Static = 2,
    Dynamic = 3,
    // Max = 4,
#pragma warning restore CS1591
}
