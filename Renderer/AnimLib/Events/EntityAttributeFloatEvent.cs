using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class EntityAttributeFloatEvent : EntityAttributeEventBase
{
    [KVProperty("m_FloatValue")]
    public ValveResourceFormat.Particles.Utils.PiecewiseCurve FloatValue { get; }
}
