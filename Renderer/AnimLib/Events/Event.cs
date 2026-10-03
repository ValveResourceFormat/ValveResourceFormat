using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class Event
{
    [KVProperty("m_flStartTime")]
    public Percent StartTime { get; }
    [KVProperty("m_flDuration")]
    public Percent Duration { get; }
    public GlobalSymbol SyncID { get; }
}
