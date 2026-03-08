using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class SyncTrack__Event
{
    public GlobalSymbol ID { get; }
    public Percent StartTime { get; }
    public Percent Duration { get; }

    public SyncTrack__Event(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        StartTime = new(data.GetProperty<KVObject>("m_startTime"));
        Duration = new(data.GetProperty<KVObject>("m_duration"));
    }
}
