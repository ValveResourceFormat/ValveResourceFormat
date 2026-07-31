using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

readonly struct SyncTrackTime
{
    public int EventIdx { get; }
    public Percent PercentageThrough { get; }


    public SyncTrackTime(int eventIdx, float percentageThrough)
    {
        EventIdx = eventIdx;
        PercentageThrough = new(percentageThrough);
    }

    public SyncTrackTime(KVObject data)
    {
        EventIdx = data.GetInt32Property("m_nEventIdx");
        PercentageThrough = new(data.GetProperty<KVObject>("m_percentageThrough"));
    }
}
