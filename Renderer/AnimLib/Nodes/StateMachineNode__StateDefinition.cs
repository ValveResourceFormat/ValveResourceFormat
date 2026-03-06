using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class StateMachineNode__StateDefinition
{
    public short StateNodeIdx { get; }
    public short EntryConditionNodeIdx { get; }
    public StateMachineNode__TransitionDefinition[] TransitionDefinitions { get; }

    public StateMachineNode__StateDefinition(KVObject data)
    {
        StateNodeIdx = data.GetInt16Property("m_nStateNodeIdx");
        EntryConditionNodeIdx = data.GetInt16Property("m_nEntryConditionNodeIdx");
        TransitionDefinitions = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_transitionDefinitions"), kv => new StateMachineNode__TransitionDefinition(kv))];
    }
}
