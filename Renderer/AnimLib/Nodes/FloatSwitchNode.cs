using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatSwitchNode : FloatValueNode
{
    public short SwitchValueNodeIdx { get; } = -1;
    public short TrueValueNodeIdx { get; } = -1;
    public short FalseValueNodeIdx { get; } = -1;
    public float FalseValue { get; }
    public float TrueValue { get; } = 1f;
}
