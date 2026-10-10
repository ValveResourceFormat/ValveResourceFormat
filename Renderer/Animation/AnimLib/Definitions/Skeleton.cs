using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>An animation skeleton: bone hierarchy, reference pose and bone masks.</summary>
[KV3Transfer]
public partial class Skeleton
{
    /// <summary>The skeleton ID.</summary>
    public GlobalSymbol ID { get; }
    /// <summary>The ID of each bone.</summary>
    public GlobalSymbol[] BoneIDs { get; } = [];
    /// <summary>The parent index of each bone, -1 for roots.</summary>
    public int[] ParentIndices { get; } = [];
    /// <summary>The reference pose with each bone relative to its parent.</summary>
    public Transform[] ParentSpaceReferencePose { get; } = [];
    /// <summary>The reference pose in model space.</summary>
    public Transform[] ModelSpaceReferencePose { get; } = [];
    /// <summary>The number of leading bones sampled at low LOD.</summary>
    [KVProperty("m_numBonesToSampleAtLowLOD")]
    public int NumBonesToSampleAtLowLOD { get; }
    /// <summary>Whether this is a prop skeleton.</summary>
    public bool IsPropSkeleton { get; }
    /// <summary>The bone mask definitions.</summary>
    public BoneMaskSetDefinition[] MaskDefinitions { get; } = [];
    /// <summary>Skeletons attached to bones of this one.</summary>
    public Skeleton.SecondarySkeleton[] SecondarySkeletons { get; } = [];
    /// <summary>The float channel sets.</summary>
    public FloatChannelSet[] FloatChannelSets { get; } = [];
    /// <summary>The contact point configurations.</summary>
    public Skeleton.ContactConfig[] ContactConfigs { get; } = [];
    /// <summary>The bones relevant to gameplay.</summary>
    public int[] GameplayRelevantBoneIndices { get; } = [];
    /// <summary>A hash of the special dependencies.</summary>
    public long SpecialDependencyHash { get; }
}

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

partial class Skeleton
{
    /// <summary>A skeleton attached to a bone of another skeleton.</summary>
    [KV3Transfer]
    public partial class SecondarySkeleton
    {
        /// <summary>The bone it attaches to.</summary>
        public GlobalSymbol AttachToBoneID { get; }
        /// <summary>The resource name of the attached skeleton.</summary>
        public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    }
}
