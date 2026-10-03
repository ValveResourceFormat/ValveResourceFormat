using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class LegacyEvent : Event
{
    public string AnimEventClassName { get; }
    public KVObject KV { get; }
}
