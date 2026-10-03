using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FootstepEventIDNode : IDValueNode
{
    public short SourceStateNodeIdx { get; }
    public BitFlags EventConditionRules { get; }

    public FootstepEventIDNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        EventConditionRules = new(data.GetProperty<KVObject>("m_eventConditionRules"));
    }
}
