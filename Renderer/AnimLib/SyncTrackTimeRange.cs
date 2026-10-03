using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A range between two sync track times.</summary>
public readonly struct SyncTrackTimeRange
{
    /// <summary>The start of the range.</summary>
    public SyncTrackTime StartTime { get; }
    /// <summary>The end of the range.</summary>
    public SyncTrackTime EndTime { get; }

    /// <summary>Creates a range from a start and end time.</summary>
    public SyncTrackTimeRange(SyncTrackTime startTime, SyncTrackTime endTime)
    {
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>Reads the range from resource data.</summary>
    public SyncTrackTimeRange(KVObject data)
    {
        StartTime = new(data.GetProperty<KVObject>("m_startTime"));
        EndTime = new(data.GetProperty<KVObject>("m_endTime"));
    }
}
