using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class SyncTrack
{
    [KV3Transfer]
    internal partial class EventMarker
    {
        public Percent StartTime { get; }
        public GlobalSymbol ID { get; }
    }
}
