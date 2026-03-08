using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class GraphEventConditionNode : BoolValueNode
{
    public short SourceStateNodeIdx { get; }
    public BitFlags EventConditionRules { get; }
    public GraphEventConditionNode__Condition[] Conditions { get; }

    public GraphEventConditionNode(KVObject data) : base(data)
    {
        SourceStateNodeIdx = data.GetInt16Property("m_nSourceStateNodeIdx");
        EventConditionRules = new(data.GetProperty<KVObject>("m_eventConditionRules"));
        Conditions = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_conditions"), kv => new GraphEventConditionNode__Condition(kv))];
    }
}
