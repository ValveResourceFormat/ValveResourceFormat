using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatRangeComparisonNode : BoolValueNode
{
    public Range Range { get; }
    public short InputValueNodeIdx { get; }
    public bool IsInclusiveCheck { get; }

    public FloatRangeComparisonNode(KVObject data) : base(data)
    {
        Range = new(data.GetProperty<KVObject>("m_range"));
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        IsInclusiveCheck = data.GetProperty<bool>("m_bIsInclusiveCheck");
    }
}
