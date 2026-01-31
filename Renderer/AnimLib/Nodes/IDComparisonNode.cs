using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class IDComparisonNode : BoolValueNode
{
    public short InputValueNodeIdx { get; }
    public IDComparisonNode__Comparison Comparison { get; }
    public GlobalSymbol[] ComparisionIDs { get; }

    public IDComparisonNode(KVObject data) : base(data)
    {
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        Comparison = data.GetEnumValue<IDComparisonNode__Comparison>("m_comparison");
        ComparisionIDs = data.GetSymbolArray("m_comparisionIDs");
    }
}
