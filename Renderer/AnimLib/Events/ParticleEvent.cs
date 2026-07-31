using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class ParticleEvent : Event
{
    public EventRelevance Relevance { get; }
    public ParticleEvent__Type Type { get; }
    public string ParticleSystem { get; } // InfoForResourceTypeIParticleSystemDefinition
    public string Tags { get; }
    public bool StopImmediately { get; }
    public bool DetachFromOwner { get; }
    public bool PlayEndCap { get; }
    public string AttachmentPoint0 { get; }
    public Particles.ParticleAttachment AttachmentType0 { get; }
    public string AttachmentPoint1 { get; }
    public Particles.ParticleAttachment AttachmentType1 { get; }
    public string Config { get; }
    public string EffectForConfig { get; }

    public ParticleEvent(KVObject data) : base(data)
    {
        Relevance = data.GetEnumValue<EventRelevance>("m_relevance");
        Type = data.GetEnumValue<ParticleEvent__Type>("m_type");
        ParticleSystem = data.GetProperty<string>("m_hParticleSystem");
        Tags = data.GetProperty<string>("m_tags");
        StopImmediately = data.GetProperty<bool>("m_bStopImmediately");
        DetachFromOwner = data.GetProperty<bool>("m_bDetachFromOwner");
        PlayEndCap = data.GetProperty<bool>("m_bPlayEndCap");
        AttachmentPoint0 = data.GetProperty<string>("m_attachmentPoint0");
        //AttachmentType0 = m_attachmentType0;
        AttachmentPoint1 = data.GetProperty<string>("m_attachmentPoint1");
        //AttachmentType1 = m_attachmentType1;
        Config = data.GetProperty<string>("m_config");
        EffectForConfig = data.GetProperty<string>("m_effectForConfig");
    }
}
