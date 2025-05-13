namespace ValveResourceFormat.CompiledShader;

public class VfxRule : ShaderDataBlock
{
    public int BlockIndex { get; }
    public ConditionalRule Rule { get; }
    public ConditionalType BlockType { get; }
    public ConditionalType[] ConditionalTypes { get; }
    public int[] Indices { get; }
    public int[] Values { get; }
    public int[] Range2 { get; }
    public string Description { get; }

    public VfxRule(ShaderDataReader datareader, int blockIndex) : base(datareader)
    {
        // CVfxRule::Unserialize
        BlockIndex = blockIndex;
        Rule = (ConditionalRule)datareader.ReadInt32();
        BlockType = (ConditionalType)datareader.ReadInt32(); // Seems weird that this would be ConditionalType, because the next 16 flags are bytes not int

        ConditionalTypes = ReadByteFlags();
        Indices = ReadIntRange();
        Values = ReadIntRange();
        Range2 = ReadIntRange();

        Description = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 256;
    }

    private int[] ReadIntRange()
    {
        var ints0 = new int[16];
        for (var i = 0; i < 16; i++)
        {
            ints0[i] = DataReader.ReadInt32();
        }
        return [.. ints0];
    }

    private ConditionalType[] ReadByteFlags()
    {
        var byteFlags = new ConditionalType[16];
        for (var i = 0; i < 16; i++)
        {
            byteFlags[i] = (ConditionalType)DataReader.ReadByte();
        }
        return byteFlags;
    }
}
