using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class Target
{
    public Transform Transform { get; }
    public GlobalSymbol BoneID { get; }
    public bool IsBoneTarget { get; }
    public bool IsUsingBoneSpaceOffsets { get; }
    public bool HasOffsets { get; }
    public bool IsSet { get; }

    public Target(KVObject data)
    {
        Transform = new(data.GetProperty<KVObject>("m_transform"));
        BoneID = data.GetProperty<string>("m_boneID");
        IsBoneTarget = data.GetProperty<bool>("m_bIsBoneTarget");
        IsUsingBoneSpaceOffsets = data.GetProperty<bool>("m_bIsUsingBoneSpaceOffsets");
        HasOffsets = data.GetProperty<bool>("m_bHasOffsets");
        IsSet = data.GetProperty<bool>("m_bIsSet");
    }
}
