using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IDEventNode : IDValueNode
{
    public short SourceStateNodeIdx { get; }
    public BitFlags EventConditionRules { get; }
    public GlobalSymbol DefaultValue { get; }

    public IDEventNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        EventConditionRules = new(data.GetProperty<KVObject>("m_eventConditionRules"));
        DefaultValue = data.GetProperty<string>("m_defaultValue");
    }
}
