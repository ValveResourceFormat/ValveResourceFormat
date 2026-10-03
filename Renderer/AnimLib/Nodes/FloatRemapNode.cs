using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatRemapNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public FloatRemapNode.RemapRange InputRange { get; }
    public FloatRemapNode.RemapRange OutputRange { get; }
}
