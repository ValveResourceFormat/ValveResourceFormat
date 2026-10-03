using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class StateMachineNode__StateDefinition
{
    public short StateNodeIdx { get; } = -1;
    public short EntryConditionNodeIdx { get; } = -1;
    public StateMachineNode__TransitionDefinition[] TransitionDefinitions { get; } = [];
}
