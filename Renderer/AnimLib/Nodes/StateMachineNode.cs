using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class StateMachineNode : PoseNode
{
    public StateMachineNode__StateDefinition[] StateDefinitions { get; }
    public short DefaultStateIndex { get; }

    public StateMachineNode(KVObject data) : base(data)
    {
        StateDefinitions = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_stateDefinitions"), kv => new StateMachineNode__StateDefinition(kv))];
        DefaultStateIndex = data.GetInt16Property("m_nDefaultStateIndex");
    }
}
