using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TransitionEventConditionNode : BoolValueNode
{
    public GlobalSymbol RequireRuleID { get; }
    public BitFlags EventConditionRules { get; }
    public short SourceStateNodeIdx { get; } = -1;
    public TransitionRuleCondition RuleCondition { get; }
}
