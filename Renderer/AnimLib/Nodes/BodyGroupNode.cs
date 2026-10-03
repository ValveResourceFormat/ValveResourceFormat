using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class BodyGroupNode : PassthroughNode
{
    public short EnabledNodeIdx { get; }
    public BodyGroupEvent Event { get; }

    // The event as sampled into the event buffer
    public NmClipEvent ClipEvent { get; }

    public BodyGroupNode(KVObject data) : base(data)
    {
        EnabledNodeIdx = data.GetInt16Property("m_nEnabledNodeIdx");
        Event = new(data.GetProperty<KVObject>("m_event"));
        ClipEvent = NmClipEvent.Build(data.GetProperty<KVObject>("m_event"), 1f);
    }
}
