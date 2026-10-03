using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

partial class Skeleton
{
    /// <summary>A contact point on a bone, probed for ground contact.</summary>
    [KV3Transfer]
    public partial class ContactConfig
    {
        /// <summary>The contact ID.</summary>
        public GlobalSymbol ID { get; }
        /// <summary>The bone the contact is on.</summary>
        public int BoneIdx { get; } = -1;
        /// <summary>The probe direction in bone space.</summary>
        [KVProperty("m_vBoneLocalProbeDir")]
        public Vector3 VBoneLocalProbeDir { get; } = new(1f, 0f, 0f);
        /// <summary>The maximum probe distance.</summary>
        public float ProbeMaxDist { get; } = 5f;
        /// <summary>The sounds the contact plays.</summary>
        public ContactAudioInfo AudioInfo { get; }
    }
}
