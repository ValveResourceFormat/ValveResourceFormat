using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class FrameSnapEvent : Event
{
    public FrameSnapEventMode FrameSnapMode { get; }

    public FrameSnapEvent(KVObject data) : base(data)
    {
        FrameSnapMode = data.GetEnumValue<FrameSnapEventMode>("m_frameSnapMode");
    }
}
