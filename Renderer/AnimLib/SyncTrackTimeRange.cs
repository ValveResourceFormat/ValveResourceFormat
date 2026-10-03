using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A range between two sync track times.</summary>
[KV3Transfer]
public readonly partial struct SyncTrackTimeRange
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
}
