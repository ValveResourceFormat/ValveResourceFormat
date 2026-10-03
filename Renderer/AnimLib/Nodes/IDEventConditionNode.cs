using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IDEventConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; }
    public BitFlags EventConditionRules { get; }
    public GlobalSymbol[] EventIDs { get; }

    public IDEventConditionNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        EventConditionRules = new(data.GetProperty<KVObject>("m_eventConditionRules"));
        EventIDs = data.GetSymbolArray("m_eventIDs");
    }
}
