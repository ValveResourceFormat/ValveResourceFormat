using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class SoundEvent : Event
{
    public EventRelevance Relevance { get; }
    public string Name { get; }
    public SoundEvent__Position Position { get; }
    public string AttachmentName { get; }
    public string Tags { get; }
    public bool ContinuePlayingSoundAtDurationEnd { get; }
    public float DurationInterruptionThreshold { get; }

    public SoundEvent(KVObject data) : base(data)
    {
        Relevance = data.GetEnumValue<EventRelevance>("m_relevance");
        Name = data.GetProperty<string>("m_name");
        Position = data.GetEnumValue<SoundEvent__Position>("m_position");
        AttachmentName = data.GetProperty<string>("m_attachmentName");
        Tags = data.GetProperty<string>("m_tags");
        ContinuePlayingSoundAtDurationEnd = data.GetProperty<bool>("m_bContinuePlayingSoundAtDurationEnd");
        DurationInterruptionThreshold = data.GetFloatProperty("m_flDurationInterruptionThreshold");
    }
}
