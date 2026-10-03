using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class BodyGroupEvent : Event
{
    public EventTargetEntity Target { get; }
    public string GroupName { get; }
    public string ChoiceName { get; }
}
