using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FootstepEventPercentageThroughNode : FloatValueNode
{
    public short SourceStateNodeIdx { get; }
    public FootPhaseCondition PhaseCondition { get; }
    public BitFlags EventConditionRules { get; }

    public FootstepEventPercentageThroughNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        PhaseCondition = data.GetEnumValue<FootPhaseCondition>("m_phaseCondition");
        EventConditionRules = new(data.GetProperty<KVObject>("m_eventConditionRules"));
    }
}
