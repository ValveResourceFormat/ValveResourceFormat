using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatSelectorNode : FloatValueNode
{
    public short[] ConditionNodeIndices { get; } = [];
    public float[] Values { get; } = [];
    public float DefaultValue { get; }
    public float EaseTime { get; } = 0.2f;
    public EasingOperation EasingOp { get; }
}
