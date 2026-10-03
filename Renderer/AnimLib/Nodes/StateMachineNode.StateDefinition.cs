using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class StateMachineNode
{
    [KV3Transfer]
    internal partial class StateDefinition
    {
        public short StateNodeIdx { get; } = -1;
        public short EntryConditionNodeIdx { get; } = -1;
        public StateMachineNode.TransitionDefinition[] TransitionDefinitions { get; } = [];
    }
}
