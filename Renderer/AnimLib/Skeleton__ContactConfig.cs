using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>A contact point on a bone, probed for ground contact.</summary>
public class Skeleton__ContactConfig
{
    /// <summary>The contact ID.</summary>
    public GlobalSymbol ID { get; }
    /// <summary>The bone the contact is on.</summary>
    public int BoneIdx { get; }
    /// <summary>The probe direction in bone space.</summary>
    public Vector3 VBoneLocalProbeDir { get; }
    /// <summary>The maximum probe distance.</summary>
    public float ProbeMaxDist { get; }
    /// <summary>The sounds the contact plays.</summary>
    public ContactAudioInfo AudioInfo { get; }

    /// <summary>Reads the config from resource data.</summary>
    public Skeleton__ContactConfig(KVObject data)
    {
        ID = data.GetProperty<string>("m_ID");
        BoneIdx = data.GetInt32Property("m_nBoneIdx");
        VBoneLocalProbeDir = data.GetSubCollection("m_vBoneLocalProbeDir").ToVector3();
        ProbeMaxDist = data.GetFloatProperty("m_flProbeMaxDist");
        AudioInfo = new(data.GetProperty<KVObject>("m_audioInfo"));
    }
}
