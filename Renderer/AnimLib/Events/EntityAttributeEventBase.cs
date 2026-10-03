using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class EntityAttributeEventBase : Event
{
    public EventTargetEntity Target { get; }
    public string AttributeName { get; }
}
