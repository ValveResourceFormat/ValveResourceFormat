using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class FloatComparisonNode : BoolValueNode
{
    public short InputValueNodeIdx { get; } = -1;
    public short ComparandValueNodeIdx { get; } = -1;
    public FloatComparisonNode__Comparison Comparison { get; }
    public float Epsilon { get; }
    public float ComparisonValue { get; }
}
