namespace ValveResourceFormat.Renderer.AnimLib;

enum TransitionRuleCondition : byte
{
    AnyAllowed = 0,
    FullyAllowed = 1,
    ConditionallyAllowed = 2,
    Blocked = 3,
}
