using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatComparisonNode : BoolValueNode
{
    public short InputValueNodeIdx { get; }
    public short ComparandValueNodeIdx { get; }
    public FloatComparisonNode__Comparison Comparison { get; }
    public float Epsilon { get; }
    public float ComparisonValue { get; }

    public FloatComparisonNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        ComparandValueNodeIdx = data.GetInt16Property("m_nComparandValueNodeIdx");
        Comparison = data.GetEnumValue<FloatComparisonNode__Comparison>("m_comparison");
        Epsilon = data.GetFloatProperty("m_flEpsilon");
        ComparisonValue = data.GetFloatProperty("m_flComparisonValue");
    }
}
