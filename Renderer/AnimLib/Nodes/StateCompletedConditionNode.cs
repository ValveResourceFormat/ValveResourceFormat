using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class StateCompletedConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; } = -1;
    public short TransitionDurationOverrideNodeIdx { get; } = -1;
    public float TransitionDurationSeconds { get; }
}
