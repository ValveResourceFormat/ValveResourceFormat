using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// One write sequence entry: a variable index paired with the register offset it is written to.
/// </summary>
/// <remarks>
/// Resource encoded shaders store each entry as one 32-bit value that is reinterpreted as this struct,
/// so the field order and size are part of the format: register offset in the low half, packed index in the high half.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 4)]
public readonly struct VfxVariableIndexData
{
    /// <summary>Gets the register offset the variable is written to.</summary>
    public short RegisterOffset { get; init; }
    /// <summary>Gets the packed variable index and layout set.</summary>
    public short PackedIndex { get; init; }

    /// <summary>Gets the variable index into <see cref="VfxProgramData.VariableDescriptions"/>.</summary>
    public int VariableIndex => PackedIndex & 0xFFF; // index VariableDescriptions
    /// <summary>Gets the descriptor set ID in the shader layout.</summary>
    public int LayoutSet => (PackedIndex >> 12) & 0xF; // Descriptor set id in the shader layout()

    /// <summary>Gets the binding slot, the low byte of the register offset. It is 255 when the variable has no register.</summary>
    public int BindingSlot => RegisterOffset & 0xFF;
    /// <summary>
    /// Gets the high byte of the register offset. Only meaningful for constant buffer bindings,
    /// where it appears to be the buffer's base register in a linearly allocated constant register space.
    /// </summary>
    public int Control => (RegisterOffset >> 8) & 0xFF;
}
