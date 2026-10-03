using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class StateNode__TimedEvent
{
    public GlobalSymbol ID { get; }
    public float TimeValueSeconds { get; }
    public StateNode__TimedEvent__Comparison ComparisionOperator { get; }
}
