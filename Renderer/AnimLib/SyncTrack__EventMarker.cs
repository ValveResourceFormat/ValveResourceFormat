using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class SyncTrack__EventMarker
{
    public Percent StartTime { get; }
    public GlobalSymbol ID { get; }

    public SyncTrack__EventMarker(KVObject data)
    {
        StartTime = new(data.GetProperty<KVObject>("m_startTime"));
        ID = data.GetProperty<string>("m_ID");
    }
}
