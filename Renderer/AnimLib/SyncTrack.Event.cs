using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

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

        /// <summary>Creates an event from an ID, start percentage and duration percentage.</summary>
        public Event(GlobalSymbol id, float startTime, float duration)
        {
            ID = id;
            StartTime = new(startTime);
            Duration = new(duration);
        }
    }
}
