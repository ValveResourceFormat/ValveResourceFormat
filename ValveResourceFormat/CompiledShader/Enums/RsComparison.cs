namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Specifies comparison functions.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/rendersystemdx11/RsComparison_t">RsComparison_t</seealso>
public enum RsComparison : byte
{
#pragma warning disable CS1591
    Never = 0,
    Less = 1,
    Equal = 2,
    LessEqual = 3,
    Greater = 4,
    NotEqual = 5,
    GreaterEqual = 6,
    Always = 7,
#pragma warning restore CS1591
}
