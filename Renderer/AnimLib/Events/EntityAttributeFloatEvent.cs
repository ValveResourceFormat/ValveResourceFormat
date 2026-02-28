using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class EntityAttributeFloatEvent : EntityAttributeEventBase
{
    public Particles.Utils.PiecewiseCurve FloatValue { get; }

    public EntityAttributeFloatEvent(KVObject data) : base(data)
    {
        FloatValue = new(data.GetProperty<KVObject>("m_FloatValue"), false);
    }
}
