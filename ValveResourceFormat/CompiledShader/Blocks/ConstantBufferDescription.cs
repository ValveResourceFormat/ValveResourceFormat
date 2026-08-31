using System.IO;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Describes an external constant buffer and its variables.
/// </summary>
public class ConstantBufferDescription : ShaderDataBlock
{
    /// <summary>
    /// Represents a variable within a constant buffer. The offset is in 4-byte scalars, vector size is the
    /// component count, row count is greater than one for matrices, and element count for arrays.
    /// </summary>
    public readonly record struct ConstantBufferVariable(string Name, int Offset, int VectorSize, int RowCount, int ElementCount);

    /// <summary>Gets the index in the owning array.</summary>
    public int Index { get; }
    /// <summary>Gets the constant buffer name.</summary>
    public string Name { get; }
    /// <summary>Gets the buffer size in bytes.</summary>
    public int BufferSize { get; }
    /// <summary>Gets whether this describes a push constant buffer.</summary>
    public bool IsPushConstantBuffer { get; }
    /// <summary>Gets the buffer type.</summary>
    public int Type { get; }
    /// <summary>Gets the array of variables in this constant buffer.</summary>
    public ConstantBufferVariable[] Variables { get; } = [];
    /// <summary>Gets the CRC32 checksum of the block.</summary>
    public uint BlockCrc { get; }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public ConstantBufferDescription(BinaryReader datareader, int index) : base(datareader)
    {
        // VfxUnserializeExternalConstantBufferDescription
        Index = index;
        Name = ReadStringWithMaxLength(datareader, 64);

        var bufferSize = datareader.ReadInt32();
        BufferSize = bufferSize & 0x7FFFFFFF;
        IsPushConstantBuffer = bufferSize < 0;
        Type = datareader.ReadInt32();

        var variableCount = datareader.ReadInt32();
        Variables = new ConstantBufferVariable[variableCount];
        for (var i = 0; i < variableCount; i++)
        {
            var name = ReadStringWithMaxLength(datareader, 64);
            var offset = datareader.ReadInt32();
            var vectorSize = datareader.ReadInt32();
            var rowCount = datareader.ReadInt32();
            var elementCount = datareader.ReadInt32();
            Variables[i] = new(name, offset, vectorSize, rowCount, elementCount);
        }

        BlockCrc = datareader.ReadUInt32();
    }
}
