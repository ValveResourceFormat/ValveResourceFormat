using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class SyncTrackTimeRange
{
    public SyncTrackTime StartTime { get; }
    public SyncTrackTime EndTime { get; }

    public SyncTrackTimeRange(KVObject data)
    {
        StartTime = new(data.GetProperty<KVObject>("m_startTime"));
        EndTime = new(data.GetProperty<KVObject>("m_endTime"));
    }
}
