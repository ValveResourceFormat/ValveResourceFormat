using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class BodyGroupNode : PassthroughNode
{
    public short EnabledNodeIdx { get; }
    public BodyGroupEvent Event { get; }

    public BodyGroupNode(KVObject data) : base(data)
    {
        EnabledNodeIdx = data.GetInt16Property("m_nEnabledNodeIdx");
        Event = new(data.GetProperty<KVObject>("m_event"));
    }
}
