using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class Skeleton__ContactConfig
{
    public GlobalSymbol ID { get; }
    public int BoneIdx { get; }
    public Vector3 VBoneLocalProbeDir { get; }
    public float ProbeMaxDist { get; }
    public ContactAudioInfo AudioInfo { get; }

    public Skeleton__ContactConfig(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        BoneIdx = data.GetInt32Property("m_nBoneIdx");
        VBoneLocalProbeDir = data.GetSubCollection("m_vBoneLocalProbeDir").ToVector3();
        ProbeMaxDist = data.GetFloatProperty("m_flProbeMaxDist");
        AudioInfo = new(data.GetProperty<KVObject>("m_audioInfo"));
    }
}
