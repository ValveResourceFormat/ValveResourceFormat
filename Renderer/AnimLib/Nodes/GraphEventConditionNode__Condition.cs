using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class GraphEventConditionNode__Condition
{
    public GlobalSymbol EventID { get; }
    public GraphEventTypeCondition EventTypeCondition { get; }

    public GraphEventConditionNode__Condition(KVObject data)
    {
        EventID = data.GetProperty<string>("m_eventID");
        EventTypeCondition = data.GetEnumValue<GraphEventTypeCondition>("m_eventTypeCondition");
    }
}
