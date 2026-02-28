using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class Event
{
    public Percent StartTime { get; }
    public Percent Duration { get; }
    public GlobalSymbol SyncID { get; }
    public bool ClientOnly { get; }

    public Event(KVObject data)
    {
        StartTime = new(data.GetProperty<KVObject>("m_flStartTime"));
        Duration = new(data.GetProperty<KVObject>("m_flDuration"));
        SyncID = data.GetProperty<string>("m_syncID");
        ClientOnly = data.GetProperty<bool>("m_bClientOnly");
    }
}
