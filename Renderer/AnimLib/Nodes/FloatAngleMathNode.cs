using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatAngleMathNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public FloatAngleMathNode__Operation Operation { get; }
}
