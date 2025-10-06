namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Methods for shader combo rules.
/// </summary>
public enum VfxRuleMethod
{
#pragma warning disable CS1591
    Unknown = 0,
    ChildOf,
    Requires,
    AllowNum
#pragma warning restore CS1591
}
