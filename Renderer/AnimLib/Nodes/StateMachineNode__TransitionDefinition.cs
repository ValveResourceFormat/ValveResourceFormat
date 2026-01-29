using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class StateMachineNode__TransitionDefinition
{
    public short TargetStateIdx { get; }
    public short ConditionNodeIdx { get; }
    public short TransitionNodeIdx { get; }
    public bool CanBeForced { get; }

    public StateMachineNode__TransitionDefinition(KVObject data)
    {
        TargetStateIdx = data.GetInt16Property("m_nTargetStateIdx");
        ConditionNodeIdx = data.GetInt16Property("m_nConditionNodeIdx");
        TransitionNodeIdx = data.GetInt16Property("m_nTransitionNodeIdx");
        CanBeForced = data.GetProperty<bool>("m_bCanBeForced");
    }
}
