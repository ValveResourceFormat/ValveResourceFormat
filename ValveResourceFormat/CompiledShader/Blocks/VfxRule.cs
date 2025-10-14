using System.Diagnostics;
using System.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Represents a constraint rule for shader combos.
/// </summary>
public class VfxRule : ShaderDataBlock
{
    /// <summary>Gets the block index.</summary>
    public int BlockIndex { get; }
    /// <summary>Gets the rule method.</summary>
    public VfxRuleMethod Rule { get; }
    /// <summary>Gets the rule type.</summary>
    public VfxRuleType RuleType { get; }
    /// <summary>Gets the array of conditional types.</summary>
    public VfxRuleType[] ConditionalTypes { get; }
    /// <summary>Gets the array of indices.</summary>
    public int[] Indices { get; }
    /// <summary>Gets the array of values.</summary>
    public int[] Values { get; }
    /// <summary>Gets extra rule data.</summary>
    public int[] ExtraRuleData { get; }
    /// <summary>Gets the error description.</summary>
    public string Description { get; }

    private const int MaxArgs = 16;

    /// <summary>
    /// Initializes a new instance from <see cref="KVObject"/> data.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
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
