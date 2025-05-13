using System.IO;

namespace ValveResourceFormat.CompiledShader;

public class ConstantBufferVariable : ShaderDataBlock
{
    public int BlockIndex { get; }
    public string Name { get; }
    public int BufferSize { get; }
    public int Arg0 { get; }
    public int ParamCount { get; }
    public List<(string Name, int Offset, int VectorSize, int Depth, int Length)> BufferParams { get; } = [];
    public uint BlockCrc { get; }

    public ConstantBufferVariable(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // VfxUnserializeExternalConstantBufferDescription
        BlockIndex = blockIndex;
        Name = ReadStringWithMaxLength(datareader, 64);

        BufferSize = datareader.ReadInt32();
        Arg0 = datareader.ReadInt32();
        ParamCount = datareader.ReadInt32();
        for (var i = 0; i < ParamCount; i++)
        {
            var paramName = ReadStringWithMaxLength(datareader, 64);
            var bufferIndex = datareader.ReadInt32();
            var arg0 = datareader.ReadInt32();
            var arg1 = datareader.ReadInt32();
            var arg2 = datareader.ReadInt32();
            BufferParams.Add((paramName, bufferIndex, arg0, arg1, arg2));
        }
        BlockCrc = datareader.ReadUInt32();
    }
}
