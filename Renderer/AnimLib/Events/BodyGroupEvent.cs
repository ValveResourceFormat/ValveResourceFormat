using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class BodyGroupEvent : Event
{
    public string GroupName { get; }
    public int GroupValue { get; }

    public BodyGroupEvent(KVObject data) : base(data)
    {
        GroupName = data.GetProperty<string>("m_groupName");
        GroupValue = data.GetInt32Property("m_nGroupValue");
    }
}
