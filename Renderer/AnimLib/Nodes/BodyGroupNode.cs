using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class BodyGroupNode : PassthroughNode
{
    public short EnabledNodeIdx { get; } = -1;
    public BodyGroupEvent Event { get; }

    // The event as sampled into the event buffer
    [KVIgnore]
    public NmClipEvent ClipEvent { get; private set; }

    partial void OnLoaded(KVObject data)
    {
        ClipEvent = NmClipEvent.Build(data.GetProperty<KVObject>("m_event"), 1f);
    }
}
