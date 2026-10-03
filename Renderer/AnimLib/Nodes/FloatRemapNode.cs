using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatRemapNode : FloatValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public FloatRemapNode__RemapRange InputRange { get; }
    public FloatRemapNode__RemapRange OutputRange { get; }
}
