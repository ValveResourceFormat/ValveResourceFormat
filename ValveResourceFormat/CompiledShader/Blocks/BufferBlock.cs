namespace ValveResourceFormat.CompiledShader;

public class BufferBlock : ShaderDataBlock
{
    public int BlockIndex { get; }
    public string Name { get; }
    public int BufferSize { get; }
    public int Arg0 { get; }
    public int ParamCount { get; }
    public List<(string Name, int Offset, int VectorSize, int Depth, int Length)> BufferParams { get; } = [];
    public uint BlockCrc { get; }

    public BufferBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
    {
        BlockIndex = blockIndex;
        Name = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 64;
        BufferSize = datareader.ReadInt32();
        // datareader.MoveOffset(4); // these 4 bytes are always 0
        Arg0 = datareader.ReadInt32();
        ParamCount = datareader.ReadInt32();
        for (var i = 0; i < ParamCount; i++)
        {
            var paramName = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            var bufferIndex = datareader.ReadInt32();
            var arg0 = datareader.ReadInt32();
            var arg1 = datareader.ReadInt32();
            var arg2 = datareader.ReadInt32();
            BufferParams.Add((paramName, bufferIndex, arg0, arg1, arg2));
        }
        BlockCrc = datareader.ReadUInt32();
    }

    public void PrintByteDetail()
    {
        DataReader.BaseStream.Position = Start;
        var blockname = DataReader.ReadNullTermStringAtPosition();
        DataReader.ShowByteCount($"BUFFER-BLOCK[{BlockIndex}] {blockname}");
        DataReader.ShowBytes(64);
        var bufferSize = DataReader.ReadUInt32AtPosition();
        DataReader.ShowBytes(4, $"{bufferSize} buffer-size");
        DataReader.ShowBytes(4);
        var paramCount = DataReader.ReadUInt32AtPosition();
        DataReader.ShowBytes(4, $"{paramCount} param-count");
        for (var i = 0; i < paramCount; i++)
        {
            var paramname = DataReader.ReadNullTermStringAtPosition();
            DataReader.OutputWriteLine($"// {paramname}");
            DataReader.ShowBytes(64);
            var paramIndex = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, breakLine: false);
            DataReader.TabComment($"{paramIndex} buffer-offset", 28);
            var vertexSize = DataReader.ReadUInt32AtPosition();
            var attributeCount = DataReader.ReadUInt32AtPosition(4);
            var size = DataReader.ReadUInt32AtPosition(8);
            DataReader.ShowBytes(12, $"({vertexSize},{attributeCount},{size}) (vertex-size, attribute-count, length)");
        }
        DataReader.BreakLine();
        DataReader.ShowBytes(4, "bufferID (some kind of crc/check)");
        DataReader.BreakLine();
        DataReader.BreakLine();
    }
}
