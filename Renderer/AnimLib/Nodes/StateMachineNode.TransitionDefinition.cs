using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class StateMachineNode
{
    [KV3Transfer]
    internal partial class TransitionDefinition
    {
        public short TargetStateIdx { get; } = -1;
        public short ConditionNodeIdx { get; } = -1;
        public short TransitionNodeIdx { get; } = -1;
        public bool CanBeForced { get; }
    }
}
