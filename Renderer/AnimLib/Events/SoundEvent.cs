using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class SoundEvent : Event
{
    public EventRelevance Relevance { get; } = EventRelevance.ClientAndServer;
    public string Name { get; }
    public SoundEvent__Position Position { get; }
    public string AttachmentName { get; }
    public string Tags { get; }
    public bool ContinuePlayingSoundAtDurationEnd { get; }
    public float DurationInterruptionThreshold { get; } = 0.9f;
}
