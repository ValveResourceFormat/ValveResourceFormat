using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class EntityAttributeEventBase : Event
{
    public EventTargetEntity Target { get; }
    public string AttributeName { get; }

    public EntityAttributeEventBase(KVObject data) : base(data)
    {
        Target = data.GetEnumValue<EventTargetEntity>("m_target");
        AttributeName = data.GetProperty<string>("m_attributeName");
    }
}
