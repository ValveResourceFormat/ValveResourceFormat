using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

readonly struct SyncTrackTimeRange
{
    public SyncTrackTime StartTime { get; }
    public SyncTrackTime EndTime { get; }


    public SyncTrackTimeRange(SyncTrackTime startTime, SyncTrackTime endTime)
    {
        StartTime = startTime;
        EndTime = endTime;
    }

    public SyncTrackTimeRange(KVObject data)
    {
        StartTime = new(data.GetProperty<KVObject>("m_startTime"));
        EndTime = new(data.GetProperty<KVObject>("m_endTime"));
    }
}
