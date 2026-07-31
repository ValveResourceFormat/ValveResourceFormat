using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IDSelectorNode : IDValueNode
{
    public short[] ConditionNodeIndices { get; }
    public GlobalSymbol[] Values { get; }
    public GlobalSymbol DefaultValue { get; }

    public IDSelectorNode(KVObject data) : base(data)
    {
        ConditionNodeIndices = data.GetArray<short>("m_conditionNodeIndices");
        Values = data.GetSymbolArray("m_values");
        DefaultValue = data.GetProperty<string>("m_defaultValue");
    }
}
