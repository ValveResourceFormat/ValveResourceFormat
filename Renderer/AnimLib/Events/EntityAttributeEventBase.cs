using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class EntityAttributeEventBase : Event
{
    public string AttributeName { get; }

    public EntityAttributeEventBase(KVObject data) : base(data)
    {
        AttributeName = data.GetProperty<string>("m_attributeName");
    }
}
