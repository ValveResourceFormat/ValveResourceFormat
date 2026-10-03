using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class StateNode
{
    [KV3Transfer]
    internal partial class TimedEvent
    {
        public GlobalSymbol ID { get; }
        public float TimeValueSeconds { get; }
        public StateNode.TimedEvent.Comparison ComparisionOperator { get; }
    }
}
