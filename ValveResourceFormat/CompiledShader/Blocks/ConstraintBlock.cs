
namespace ValveResourceFormat.CompiledShader;

// ConstraintBlocks are always 472 bytes long
public class ConstraintBlock : ShaderDataBlock
{
    public int BlockIndex { get; }
    public ConditionalRule Rule { get; }
    public ConditionalType BlockType { get; }
    public ConditionalType[] ConditionalTypes { get; }
    public int[] Indices { get; }
    public int[] Values { get; }
    public int[] Range2 { get; }
    public string Description { get; }

    public ConstraintBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
    {
        BlockIndex = blockIndex;
        Rule = (ConditionalRule)datareader.ReadInt32();
        BlockType = (ConditionalType)datareader.ReadInt32();
        ConditionalTypes = Array.ConvertAll(ReadByteFlags(), x => (ConditionalType)x);

        Indices = ReadIntRange();
        datareader.BaseStream.Position += 68 - Indices.Length * 4;
        Values = ReadIntRange();
        datareader.BaseStream.Position += 60 - Values.Length * 4;

        Range2 = ReadIntRange();
        datareader.BaseStream.Position += 64 - Range2.Length * 4;
        Description = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 256;
    }

    public ConstraintBlock(ShaderDataReader datareader, int blockIndex, ConditionalType conditionalTypeVerify)
        : this(datareader, blockIndex)
    {
        if (BlockType != conditionalTypeVerify)
        {
            throw new UnexpectedMagicException($"Unexpected {nameof(BlockType)}", $"{BlockType}", nameof(BlockType));
        }
    }

    private int[] ReadIntRange()
    {
        List<int> ints0 = [];
        while (DataReader.ReadInt32AtPosition() >= 0)
        {
            ints0.Add(DataReader.ReadInt32());
        }
        return [.. ints0];
    }

    private int[] ReadByteFlags()
    {
        var count = 0;
        var savedPosition = DataReader.BaseStream.Position;
        while (DataReader.ReadByte() > 0 && count < 16)
        {
            count++;
        }
        var byteFlags = new int[count];
        DataReader.BaseStream.Position = savedPosition;
        for (var i = 0; i < count; i++)
        {
            byteFlags[i] = DataReader.ReadByte();
        }
        DataReader.BaseStream.Position = savedPosition + 16;
        return byteFlags;
    }

    public void PrintByteDetail()
    {
        DataReader.BaseStream.Position = Start;
        DataReader.ShowByteCount($"{BlockType}-CONTRAINTS-BLOCK[{BlockIndex}]");
        DataReader.ShowBytes(216);
        var name1 = DataReader.ReadNullTermStringAtPosition();
        DataReader.OutputWriteLine($"[{DataReader.BaseStream.Position}] {name1}");
        DataReader.ShowBytes(256);
        DataReader.BreakLine();
    }
}
