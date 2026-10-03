using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class IDComparisonNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public IDComparisonNode__Comparison Comparison { get; }
    public GlobalSymbol[] ComparisionIDs { get; } = [];
}
