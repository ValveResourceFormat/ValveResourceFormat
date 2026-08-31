namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// One write sequence entry: a variable index paired with the register offset it is written to.
/// </summary>
public readonly struct VfxVariableIndexData
{
    /// <summary>Gets the register offset the variable is written to.</summary>
    public short RegisterOffset { get; init; }
    /// <summary>Gets the packed variable index and layout set.</summary>
    public short PackedIndex { get; init; }

    /// <summary>Gets the variable index into <see cref="VfxProgramData.VariableDescriptions"/>.</summary>
    public int VariableIndex => PackedIndex & 0xFFF; // index VariableDescriptions
    /// <summary>Gets the descriptor set ID in the shader layout.</summary>
    public int LayoutSet => PackedIndex >> 12; // Descriptor set id in the shader layout()

    /// <summary>Gets the binding slot, the low byte of the register offset. It is 255 when the variable has no register.</summary>
    public int BindingSlot => RegisterOffset & 0xFF;
    /// <summary>
    /// Gets the high byte of the register offset. Only meaningful for constant buffer bindings,
    /// where it appears to be the buffer's base register in a linearly allocated constant register space.
    /// </summary>
    public int Control => (RegisterOffset >> 8) & 0xFF;
}
