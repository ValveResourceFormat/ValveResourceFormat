namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Contains variable index and layout information.
/// </summary>
public readonly struct VfxVariableIndexData
{
    /// <summary>Gets the second field containing dest and control.</summary>
    public short Field2 { get; init; }
    /// <summary>Gets the first field containing variable index and layout set.</summary>
    public short Field1 { get; init; }

    /// <summary>Gets the variable index into VariableDescriptions.</summary>
    public int VariableIndex => Field1 & 0xFFF; // index VariableDescriptions
    /// <summary>Gets the descriptor set ID in the shader layout.</summary>
    public int LayoutSet => Field1 >> 12; // Descriptor set id in the shader layout()

    /// <summary>Gets the destination value.</summary>
    public int Dest => Field2 & 0xFF;
    /// <summary>Gets the control value.</summary>
    public int Control => (Field2 >> 8) & 0xFF;
}
