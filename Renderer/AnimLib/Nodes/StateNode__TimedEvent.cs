using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class StateNode__TimedEvent
{
    public GlobalSymbol ID { get; }
    public float TimeValueSeconds { get; }
    public StateNode__TimedEvent__Comparison ComparisionOperator { get; }

    public StateNode__TimedEvent(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        TimeValueSeconds = data.GetFloatProperty("m_flTimeValueSeconds");
        ComparisionOperator = data.GetEnumValue<StateNode__TimedEvent__Comparison>("m_comparisionOperator");
    }
}
