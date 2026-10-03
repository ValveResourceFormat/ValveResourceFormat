using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatEaseNode : FloatValueNode
{
    public float EaseTime { get; } = 1f;
    public float StartValue { get; }
    public short InputValueNodeIdx { get; } = -1;
    public EasingOperation EasingOp { get; }
    public bool UseStartValue { get; }
}
