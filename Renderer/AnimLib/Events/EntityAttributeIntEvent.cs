using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class EntityAttributeIntEvent : EntityAttributeEventBase
{
    public int IntValue { get; }

    public EntityAttributeIntEvent(KVObject data) : base(data)
    {
        IntValue = data.GetInt32Property("m_nIntValue");
    }
}
