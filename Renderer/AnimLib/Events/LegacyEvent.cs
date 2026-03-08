using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class LegacyEvent : Event
{
    public string AnimEventClassName { get; }
    public KVObject KV { get; }

    public LegacyEvent(KVObject data) : base(data)
    {
        AnimEventClassName = data.GetProperty<string>("m_animEventClassName");
        KV = data.GetProperty<KVObject>("m_KV");
    }
}
