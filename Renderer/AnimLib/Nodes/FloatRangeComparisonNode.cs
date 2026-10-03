using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatRangeComparisonNode : BoolValueNode
{
    public Range Range { get; }
    public short InputValueNodeIdx { get; } = -1;
    public bool IsInclusiveCheck { get; } = true;
}
