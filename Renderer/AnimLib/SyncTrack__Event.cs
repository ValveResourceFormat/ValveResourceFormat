using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A named sync period of a sync track.</summary>
public readonly struct SyncTrack__Event
{
    /// <summary>The event ID.</summary>
    public GlobalSymbol ID { get; }
    /// <summary>Where the event starts, as a percentage through the track.</summary>
    public Percent StartTime { get; }
    /// <summary>How long the event lasts, as a percentage of the track.</summary>
    public Percent Duration { get; }

    /// <summary>Creates an event from an ID, start percentage and duration percentage.</summary>
    public SyncTrack__Event(GlobalSymbol id, float startTime, float duration)
    {
        ID = id;
        StartTime = new(startTime);
        Duration = new(duration);
    }

    /// <summary>Reads the event from resource data.</summary>
    public SyncTrack__Event(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        StartTime = new(data.GetProperty<KVObject>("m_startTime"));
        Duration = new(data.GetProperty<KVObject>("m_duration"));
    }
}
