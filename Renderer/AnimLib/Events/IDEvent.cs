using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class IDEvent : Event
{
    public GlobalSymbol ID { get; }
    public GlobalSymbol SecondaryID { get; }
}
