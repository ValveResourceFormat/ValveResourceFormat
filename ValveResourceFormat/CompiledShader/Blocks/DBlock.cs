namespace ValveResourceFormat.CompiledShader;

// DBlocks are always 152 bytes long
public class DBlock : ShaderDataBlock, ICombo
{
    public int BlockIndex { get; }
    public string Name { get; }
    public string Category { get; } // it looks like d-blocks might have the provision for a "category" (but not seen in use)
    public int Arg0 { get; }
    public int RangeMin { get; }
    public int RangeMax { get; }
    public int Arg3 { get; }
    public int FeatureIndex { get; }
    public int Arg5 { get; }

    public DBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
    {
        BlockIndex = blockIndex;
        Name = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 64;
        Category = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 64;
        Arg0 = datareader.ReadInt32();
        RangeMin = datareader.ReadInt32();
        RangeMax = datareader.ReadInt32();
        Arg3 = datareader.ReadInt32();
        FeatureIndex = datareader.ReadInt32();
        Arg5 = datareader.ReadInt32();
    }

    public void PrintByteDetail()
    {
        DataReader.BaseStream.Position = Start;
        var dBlockName = DataReader.ReadNullTermStringAtPosition();
        DataReader.ShowByteCount($"D-BLOCK[{BlockIndex}]");
        DataReader.Comment(dBlockName);
        DataReader.ShowBytes(128);
        DataReader.ShowBytes(12, 4);
        DataReader.ShowBytes(12);
        DataReader.BreakLine();
    }
}
