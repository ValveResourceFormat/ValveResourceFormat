using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class SyncTrack
{
    public SyncTrack__Event[] SyncEvents { get; }
    public int StartEventOffset { get; }

    public SyncTrack(KVObject data)
    {
        SyncEvents = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_syncEvents"), kv => new SyncTrack__Event(kv))];
        StartEventOffset = data.GetInt32Property("m_nStartEventOffset");
    }
}
