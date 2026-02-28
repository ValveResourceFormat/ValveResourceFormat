using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IDEventPercentageThroughNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; }
    public BitFlags EventConditionRules { get; }
    public GlobalSymbol EventID { get; }

    public IDEventPercentageThroughNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        EventConditionRules = new(data.GetProperty<KVObject>("m_eventConditionRules"));
        EventID = data.GetProperty<string>("m_eventID");
    }
}
