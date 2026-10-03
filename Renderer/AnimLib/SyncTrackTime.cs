using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A position on a sync track: an event index and a percentage through that event.</summary>
public readonly struct SyncTrackTime
{
    /// <summary>The event index.</summary>
    public int EventIdx { get; }
    /// <summary>How far through the event the position is.</summary>
    public Percent PercentageThrough { get; }

    /// <summary>Creates a time from an event index and a percentage through it.</summary>
    public SyncTrackTime(int eventIdx, float percentageThrough)
    {
        EventIdx = eventIdx;
        PercentageThrough = new(percentageThrough);
    }

    /// <summary>Reads the time from resource data.</summary>
    public SyncTrackTime(KVObject data)
    {
        EventIdx = data.GetInt32Property("m_nEventIdx");
        PercentageThrough = new(data.GetProperty<KVObject>("m_percentageThrough"));
    }
}
