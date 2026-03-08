using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class TimeConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; }
    public short InputValueNodeIdx { get; }
    public float Comparand { get; }
    public TimeConditionNode__ComparisonType Type { get; }
    public TimeConditionNode__Operator Operator { get; }

    public TimeConditionNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_sourceStateNodeIdx");
        InputValueNodeIdx = data.GetInt16Property("m_nInputValueNodeIdx");
        Comparand = data.GetFloatProperty("m_flComparand");
        Type = data.GetEnumValue<TimeConditionNode__ComparisonType>("m_type");
        Operator = data.GetEnumValue<TimeConditionNode__Operator>("m_operator");
    }
}
