using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>
/// Synchronizes animations by dividing their timeline into named sync periods. Positions on the
/// track are expressed as <see cref="SyncTrackTime"/> (event index plus percentage through it),
/// which lets differently-timed clips advance in lockstep. Port of Esoterica's SyncTrack.
/// </summary>
[KV3Transfer]
public partial class SyncTrack
{
    /// <summary>The sync events, in order.</summary>
    public SyncTrack.Event[] SyncEvents { get; private set; } = [];
    /// <summary>The index of the event playback starts at.</summary>
    public int StartEventOffset { get; private set; }
}

partial class SyncTrack
{
    /// <summary>A named sync period of a sync track.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Named after the sync track event it is read from")]
    [KV3Transfer]
    public readonly partial struct Event
    {
        /// <summary>The event ID.</summary>
        public GlobalSymbol ID { get; }
        /// <summary>Where the event starts, as a percentage through the track.</summary>
        public Percent StartTime { get; }
        /// <summary>How long the event lasts, as a percentage of the track.</summary>
        public Percent Duration { get; }
    }
}

partial class SyncTrack
{
    [KV3Transfer]
    internal partial class EventMarker
    {
        public Percent StartTime { get; }
        public GlobalSymbol ID { get; }
    }
}

/// <summary>A position on a sync track: an event index and a percentage through that event.</summary>
[KV3Transfer]
public readonly partial struct SyncTrackTime
{
    /// <summary>The event index.</summary>
    public int EventIdx { get; }
    /// <summary>How far through the event the position is.</summary>
    public Percent PercentageThrough { get; }
}

/// <summary>A range between two sync track times.</summary>
[KV3Transfer]
public readonly partial struct SyncTrackTimeRange
{
    /// <summary>The start of the range.</summary>
    public SyncTrackTime StartTime { get; }
    /// <summary>The end of the range.</summary>
    public SyncTrackTime EndTime { get; }
}
