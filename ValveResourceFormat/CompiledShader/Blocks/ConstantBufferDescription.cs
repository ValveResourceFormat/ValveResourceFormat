using System.IO;

namespace ValveResourceFormat.CompiledShader;

public class ConstantBufferDescription : ShaderDataBlock
{
    public readonly record struct ConstantBufferVariable(string Name, int Offset, int VectorSize, int Depth, int Length);

    public int BlockIndex { get; }
    public string Name { get; }
    public int BufferSize { get; }
    public int Type { get; }
    public ConstantBufferVariable[] Variables { get; } = [];
    public uint BlockCrc { get; }

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
