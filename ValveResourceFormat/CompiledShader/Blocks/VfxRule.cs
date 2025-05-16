using System.IO;

namespace ValveResourceFormat.CompiledShader;

public class VfxRule : ShaderDataBlock
{
    public int BlockIndex { get; }
    public VfxRuleMethod Rule { get; }
    public VfxRuleType RuleType { get; }
    public VfxRuleType[] ConditionalTypes { get; }
    public int[] Indices { get; }
    public int[] Values { get; }
    public int[] Range2 { get; }
    public string Description { get; }

    public VfxRule(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // CVfxRule::Unserialize
        BlockIndex = blockIndex;
        Rule = (VfxRuleMethod)datareader.ReadInt32();
        RuleType = (VfxRuleType)datareader.ReadInt32();

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

    private static VfxRuleType[] ReadByteFlags(BinaryReader datareader)
    {
        var byteFlags = new VfxRuleType[16];
        for (var i = 0; i < 16; i++)
        {
            byteFlags[i] = (VfxRuleType)datareader.ReadByte();
        }
        return byteFlags;
    }
}
