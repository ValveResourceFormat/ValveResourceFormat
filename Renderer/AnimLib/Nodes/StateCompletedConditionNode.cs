using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class StateCompletedConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; }
    public short TransitionDurationOverrideNodeIdx { get; }
    public float TransitionDurationSeconds { get; }

    public StateCompletedConditionNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        TransitionDurationOverrideNodeIdx = data.GetInt16Property("m_nTransitionDurationOverrideNodeIdx");
        TransitionDurationSeconds = data.GetFloatProperty("m_flTransitionDurationSeconds");
    }
}
