namespace ValveResourceFormat.CompiledShader;

#pragma warning disable CS1591 // Enum members are self-describing

/// <summary>
/// Specifies the fill mode for rendering.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/rendersystemdx11/RsFillMode_t">RsFillMode_t</seealso>
public enum RsFillMode : byte
{
    Solid = 0,
    Wireframe = 1,
}

/// <summary>
/// Specifies the cull mode for rendering.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/rendersystemdx11/RsCullMode_t">RsCullMode_t</seealso>
public enum RsCullMode : byte
{
    None = 0,
    Back = 1,
    Front = 2,
}

/// <summary>
/// Specifies stencil operations.
/// </summary>
public enum RsStencilOp : byte
{
    Keep = 0,
    Zero = 1,
    Replace = 2,
    IncrSat = 3,
    DecrSat = 4,
    Invert = 5,
    Incr = 6,
    Decr = 7,
}

/// <summary>
/// Specifies blend modes.
/// </summary>
public enum RsBlendMode : byte
{
    Zero = 0,
    One = 1,
    SrcColor = 2,
    InvSrcColor = 3,
    SrcAlpha = 4,
    InvSrcAlpha = 5,
    DestAlpha = 6,
    InvDestAlpha = 7,
    DestColor = 8,
    InvDestColor = 9,
    SrcAlphaSat = 10,
    BlendFactor = 11,
    InvBlendFactor = 12,
}

/// <summary>
/// Specifies blend operations.
/// </summary>
public enum RsBlendOp : byte
{
    Add = 0,
    Subtract = 1,
    RevSubtract = 2,
    Min = 3,
    Max = 4,
}

/// <summary>
/// Specifies color write enable flags.
/// </summary>
[Flags]
public enum RsColorWriteEnableBits : byte
{
    None = 0,
    R = 0x1,
    G = 0x2,
    B = 0x4,
    A = 0x8,
    All = R | G | B | A
}
