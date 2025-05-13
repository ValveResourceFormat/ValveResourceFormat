using System.IO;

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

    public VfxRule(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // CVfxRule::Unserialize
        BlockIndex = blockIndex;
        Rule = (ConditionalRule)datareader.ReadInt32();
        BlockType = (ConditionalType)datareader.ReadInt32(); // Seems weird that this would be ConditionalType, because the next 16 flags are bytes not int

        ConditionalTypes = ReadByteFlags(datareader);
        Indices = ReadIntRange(datareader);
        Values = ReadIntRange(datareader);
        Range2 = ReadIntRange(datareader);

        Description = ReadStringWithMaxLength(datareader, 256);
    }

    private static int[] ReadIntRange(BinaryReader datareader)
    {
        var ints0 = new int[16];
        for (var i = 0; i < 16; i++)
        {
            ints0[i] = datareader.ReadInt32();
        }
        return [.. ints0];
    }

    private static ConditionalType[] ReadByteFlags(BinaryReader datareader)
    {
        var byteFlags = new ConditionalType[16];
        for (var i = 0; i < 16; i++)
        {
            byteFlags[i] = (ConditionalType)datareader.ReadByte();
        }
        return byteFlags;
    }
}
