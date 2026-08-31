using System.Diagnostics;
using System.IO;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Represents a constraint rule for shader combos.
/// </summary>
public class VfxRule : ShaderDataBlock
{
    /// <summary>Gets the index in the owning array.</summary>
    public int Index { get; }
    /// <summary>Gets the rule method.</summary>
    public VfxRuleMethod RuleMethod { get; }
    /// <summary>Gets the rule type.</summary>
    public VfxRuleType RuleType { get; }
    /// <summary>Gets the type of each rule argument.</summary>
    public VfxRuleType[] ArgTypes { get; }
    /// <summary>Gets the combo or feature index of each rule argument.</summary>
    public int[] ArgIndices { get; }
    /// <summary>Gets the value of each rule argument.</summary>
    public int[] ArgValues { get; }
    /// <summary>Gets extra rule data.</summary>
    public int[] ExtraRuleData { get; }
    /// <summary>Gets the error message shown when the rule is violated.</summary>
    public string ErrorString { get; }

    private const int MaxArgs = 16;

    /// <summary>
    /// Initializes a new instance from <see cref="KVObject"/> data.
    /// </summary>
    public VfxRule(KVObject data, int index) : base()
    {
        Index = index;
        RuleMethod = data.GetEnumValue<VfxRuleMethod>("m_rule", normalize: true, stripExtension: "Method");
        RuleType = data.GetEnumValue<VfxRuleType>("m_ruleType", normalize: true);

        ArgTypes = new VfxRuleType[MaxArgs];
        ArgIndices = new int[MaxArgs];
        ArgValues = new int[MaxArgs];
        ExtraRuleData = new int[MaxArgs];

        var argTypesArray = data.GetArray<string>("m_argTypeArray")!;
        var argIndexArray = data.GetArray<int>("m_argIndexArray")!;
        var argValueArray = data.GetArray<int>("m_argValueArray")!;
        var extraRuleData = data.GetArray<int>("m_nExtraRuleData")!;

        Debug.Assert(argTypesArray.Length == MaxArgs);
        Debug.Assert(argIndexArray.Length == MaxArgs);
        Debug.Assert(argValueArray.Length == MaxArgs);
        Debug.Assert(extraRuleData.Length == MaxArgs);

        for (var i = 0; i < MaxArgs; i++)
        {
            ArgTypes[i] = Enum.Parse<VfxRuleType>(KVObjectExtensions.NormalizeEnumName<VfxRuleType>(argTypesArray[i]));
            ArgIndices[i] = argIndexArray[i];
            ArgValues[i] = argValueArray[i];
            ExtraRuleData[i] = extraRuleData[i];
        }

        ErrorString = data.GetStringProperty("m_szErrorString");
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxRule(BinaryReader datareader, int index) : base(datareader)
    {
        // CVfxRule::Unserialize
        Index = index;
        RuleMethod = (VfxRuleMethod)datareader.ReadInt32();
        RuleType = (VfxRuleType)datareader.ReadInt32();

        ArgTypes = ReadArgTypes(datareader);
        ArgIndices = ReadArgArray(datareader);
        ArgValues = ReadArgArray(datareader);
        ExtraRuleData = ReadArgArray(datareader);

        ErrorString = ReadStringWithMaxLength(datareader, 256);
    }

    private static int[] ReadArgArray(BinaryReader datareader)
    {
        var values = new int[MaxArgs];
        for (var i = 0; i < MaxArgs; i++)
        {
            values[i] = datareader.ReadInt32();
        }
        return values;
    }

    private static VfxRuleType[] ReadArgTypes(BinaryReader datareader)
    {
        var argTypes = new VfxRuleType[MaxArgs];
        for (var i = 0; i < MaxArgs; i++)
        {
            argTypes[i] = (VfxRuleType)datareader.ReadByte();
        }
        return argTypes;
    }
}
