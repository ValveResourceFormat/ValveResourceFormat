using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TransitionEventConditionNode : BoolValueNode
{
    public GlobalSymbol RequireRuleID { get; }
    public BitFlags EventConditionRules { get; }
    public short SourceStateNodeIdx { get; }
    public TransitionRuleCondition RuleCondition { get; }

    public TransitionEventConditionNode(KVObject data) : base(data)
    {
        RequireRuleID = data.GetProperty<string>("m_requireRuleID");
        EventConditionRules = new(data.GetProperty<KVObject>("m_eventConditionRules"));
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        RuleCondition = data.GetEnumValue<TransitionRuleCondition>("m_ruleCondition");
    }
}
