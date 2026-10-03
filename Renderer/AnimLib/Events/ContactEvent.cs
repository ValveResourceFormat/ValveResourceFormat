using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class ContactEvent : Event
{
    public GlobalSymbol ConfigID { get; }
    public GlobalSymbol ProbeBoneID { get; }
    public Vector3 VBoneLocalProbeDir { get; }
    public float ProbeMaxDist { get; }
    public ContactAudioInfo AudioInfo { get; }

    public ContactEvent(KVObject data) : base(data)
    {
        ConfigID = data.GetProperty<string>("m_configID");
        ProbeBoneID = data.GetProperty<string>("m_probeBoneID");
        VBoneLocalProbeDir = data.GetSubCollection("m_vBoneLocalProbeDir").ToVector3();
        ProbeMaxDist = data.GetFloatProperty("m_flProbeMaxDist");
        AudioInfo = new(data.GetProperty<KVObject>("m_audioInfo"));
    }
}
