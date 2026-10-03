using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class ContactEvent : Event
{
    public GlobalSymbol ConfigID { get; }
    public GlobalSymbol ProbeBoneID { get; }
    [KVProperty("m_vBoneLocalProbeDir")]
    public Vector3 VBoneLocalProbeDir { get; } = new(1f, 0f, 0f);
    public float ProbeMaxDist { get; } = 3f;
    public ContactAudioInfo AudioInfo { get; }
}
