using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ParticleEvent : Event
{
    public EventRelevance Relevance { get; } = EventRelevance.ClientAndServer;
    public ParticleEvent.ParticleEventType Type { get; }
    public EventTargetEntity Target { get; }
    [KVProperty("m_hParticleSystem")]
    public string ParticleSystem { get; } // InfoForResourceTypeIParticleSystemDefinition
    public string Tags { get; }
    public bool StopImmediately { get; }
    public bool DetachFromOwner { get; }
    public bool PlayEndCap { get; }
    public string AttachmentPoint0 { get; }
    public ValveResourceFormat.Particles.ParticleAttachment AttachmentType0 { get; }
    public string AttachmentPoint1 { get; }
    public ValveResourceFormat.Particles.ParticleAttachment AttachmentType1 { get; }
    public string Config { get; }
    public string EffectForConfig { get; }
}
