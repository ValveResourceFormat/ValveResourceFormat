using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TimeConditionNode : BoolValueNode
{
    [KVProperty("m_sourceStateNodeIdx")]
    public short SourceStateNodeIdx { get; } = -1;
    public short InputValueNodeIdx { get; } = -1;
    public float Comparand { get; }
    public TimeConditionNode.ComparisonType Type { get; } = TimeConditionNode.ComparisonType.ElapsedTime;
    public TimeConditionNode.OperatorType Operator { get; }
}
