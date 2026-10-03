using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class BodyGroupEvent : Event
{
    public EventTargetEntity Target { get; }
    public string GroupName { get; }
    public string ChoiceName { get; }

    public BodyGroupEvent(KVObject data) : base(data)
    {
        Target = data.GetEnumValue<EventTargetEntity>("m_target");
        GroupName = data.GetProperty<string>("m_groupName");
        ChoiceName = data.GetProperty<string>("m_choiceName");
    }
}
