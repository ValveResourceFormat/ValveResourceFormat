using System.Diagnostics;
using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

struct Target
{
    /// <summary>
    /// Either the actual transform or the offsets that need to be applied
    /// </summary>
    public Transform Transform { get; set; }
    public GlobalSymbol BoneID { get; }
    public bool IsBoneTarget { get; }
    public bool IsUsingBoneSpaceOffsets { get; set; }
    public bool HasOffsets { get; set; }
    public bool IsSet { get; set; }

    public Target(KVObject data)
    {
        Transform = new(data.GetProperty<KVObject>("m_transform"));
        BoneID = data.GetProperty<string>("m_boneID");
        IsBoneTarget = data.GetProperty<bool>("m_bIsBoneTarget");
        IsUsingBoneSpaceOffsets = data.GetProperty<bool>("m_bIsUsingBoneSpaceOffsets");
        HasOffsets = data.GetProperty<bool>("m_bHasOffsets");
        IsSet = data.GetProperty<bool>("m_bIsSet");
    }

    public void SetOffsets(Quaternion rotationOffset, Vector3 translationOffset, bool isBoneSpaceOffset)
    {
        Debug.Assert(IsSet && IsBoneTarget); // Offsets only make sense for bone targets

        Transform = new(translationOffset, 1f, rotationOffset);
        IsUsingBoneSpaceOffsets = isBoneSpaceOffset;
        HasOffsets = true;
    }
}
