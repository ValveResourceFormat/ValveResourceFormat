using System.Diagnostics;
using System.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

public class VfxRule : ShaderDataBlock
{
    public int BlockIndex { get; }
    public VfxRuleMethod Rule { get; }
    public VfxRuleType RuleType { get; }
    public VfxRuleType[] ConditionalTypes { get; }
    public int[] Indices { get; }
    public int[] Values { get; }
    public int[] ExtraRuleData { get; }
    public string Description { get; }

    private const int MaxArgs = 16;

    public VfxRule(KVObject data, int blockIndex) : base()
    {
        BlockIndex = blockIndex;
        Rule = data.GetEnumValue<VfxRuleMethod>("m_rule", normalize: true, stripExtension: "Method");
        RuleType = data.GetEnumValue<VfxRuleType>("m_ruleType", normalize: true);

        ConditionalTypes = new VfxRuleType[MaxArgs];
        Indices = new int[MaxArgs];
        Values = new int[MaxArgs];
        ExtraRuleData = new int[MaxArgs];

        var argTypesArray = data.GetArray<string>("m_argTypeArray");
        var argIndexArray = data.GetArray<int>("m_argIndexArray");
        var argValueArray = data.GetArray<int>("m_argValueArray");
        var extraRuleData = data.GetArray<int>("m_nExtraRuleData");

        Debug.Assert(argTypesArray.Length == MaxArgs);
        Debug.Assert(argIndexArray.Length == MaxArgs);
        Debug.Assert(argValueArray.Length == MaxArgs);

        for (var i = 0; i < MaxArgs; i++)
        {
            ConditionalTypes[i] = Enum.Parse<VfxRuleType>(KVObjectExtensions.NormalizeEnumName<VfxRuleType>(argTypesArray[i]));
            Indices[i] = argIndexArray[i];
            Values[i] = argValueArray[i];
            ExtraRuleData[i] = extraRuleData[i];
        }

        Description = data.GetProperty<string>("m_szErrorString");
    }

    public VfxRule(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // CVfxRule::Unserialize
        BlockIndex = blockIndex;
        Rule = (VfxRuleMethod)datareader.ReadInt32();
        RuleType = (VfxRuleType)datareader.ReadInt32();

        ConditionalTypes = ReadByteFlags(datareader);
        Indices = ReadIntRange(datareader);
        Values = ReadIntRange(datareader);
        ExtraRuleData = ReadIntRange(datareader);

        Description = ReadStringWithMaxLength(datareader, 256);
    }

    private static int[] ReadIntRange(BinaryReader datareader)
    {
        var ints0 = new int[MaxArgs];
        for (var i = 0; i < MaxArgs; i++)
        {
            ints0[i] = datareader.ReadInt32();
        }
        return ints0;
    }

    private static VfxRuleType[] ReadByteFlags(BinaryReader datareader)
    {
        var byteFlags = new VfxRuleType[MaxArgs];
        for (var i = 0; i < MaxArgs; i++)
        {
            byteFlags[i] = (VfxRuleType)datareader.ReadByte();
        }
        return byteFlags;
    }
}
