using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FootEventConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; }
    public FootPhaseCondition PhaseCondition { get; }
    public BitFlags EventConditionRules { get; }

    public FootEventConditionNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        PhaseCondition = data.GetEnumValue<FootPhaseCondition>("m_phaseCondition");
        EventConditionRules = new(data.GetProperty<KVObject>("m_eventConditionRules"));
    }
}
