using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class TransitionEvent : Event
{
    public TransitionRule Rule { get; }
    public GlobalSymbol ID { get; }

    public TransitionEvent(KVObject data) : base(data)
    {
        Rule = data.GetEnumValue<TransitionRule>("m_rule");
        ID = data.GetProperty<string>("m_ID");
    }
}
