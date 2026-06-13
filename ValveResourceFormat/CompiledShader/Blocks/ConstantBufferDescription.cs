using System.IO;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Describes an external constant buffer and its variables.
/// </summary>
public class ConstantBufferDescription : ShaderDataBlock
{
    /// <summary>
    /// Represents a variable within a constant buffer.
    /// </summary>
    public readonly record struct ConstantBufferVariable(string Name, int Offset, int VectorSize, int Depth, int Length);

    /// <summary>Gets the block index.</summary>
    public int BlockIndex { get; }
    /// <summary>Gets the constant buffer name.</summary>
    public string Name { get; }
    /// <summary>Gets the buffer size in bytes.</summary>
    public int BufferSize { get; }
    /// <summary>Gets the buffer type.</summary>
    public int Type { get; }
    /// <summary>Gets the array of variables in this constant buffer.</summary>
    public ConstantBufferVariable[] Variables { get; } = [];
    /// <summary>Gets the CRC32 checksum of the block.</summary>
    public uint BlockCrc { get; }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public ConstantBufferDescription(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // VfxUnserializeExternalConstantBufferDescription
        BlockIndex = blockIndex;
        Name = ReadStringWithMaxLength(datareader, 64);

        BufferSize = datareader.ReadInt32();
        Type = datareader.ReadInt32();

        var variableCount = datareader.ReadInt32();
        Variables = new ConstantBufferVariable[variableCount];
        for (var i = 0; i < variableCount; i++)
        {
            var paramName = ReadStringWithMaxLength(datareader, 64);
            var bufferIndex = datareader.ReadInt32();
            var arg0 = datareader.ReadInt32();
            var arg1 = datareader.ReadInt32();
            var arg2 = datareader.ReadInt32();
            Variables[i] = new(paramName, bufferIndex, arg0, arg1, arg2);
        }

        BlockCrc = datareader.ReadUInt32();
    }
}
