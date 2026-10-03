using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class StateMachineNode : PoseNode
{
    public StateMachineNode__StateDefinition[] StateDefinitions { get; } = [];
    public short DefaultStateIndex { get; } = -1;
}
