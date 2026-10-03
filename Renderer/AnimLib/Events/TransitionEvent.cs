using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class TransitionEvent : Event
{
    public TransitionRule Rule { get; } = TransitionRule.BlockTransition;
    public GlobalSymbol ID { get; }
}
