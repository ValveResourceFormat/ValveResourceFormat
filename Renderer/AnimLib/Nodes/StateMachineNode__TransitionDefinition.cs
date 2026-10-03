using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class StateMachineNode__TransitionDefinition
{
    public short TargetStateIdx { get; } = -1;
    public short ConditionNodeIdx { get; } = -1;
    public short TransitionNodeIdx { get; } = -1;
    public bool CanBeForced { get; }
}
