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

    public readonly bool TryGetTransform(Pose pose, out Transform result)
    {
        if (!IsBoneTarget)
        {
            result = Transform; // Just use the internal transform
            return true;
        }

        Debug.Assert(pose != null && pose.Skeleton != null);
        var skeleton = pose.Skeleton;

        var boneIdx = Array.IndexOf(skeleton.BoneIDs, BoneID);
        if (boneIdx < 0)
        {
            result = default;
            return false;
        }

        if (HasOffsets)
        {
            // Get the local transform and the parent global transform
            if (IsUsingBoneSpaceOffsets)
            {
                result = pose.GetTransform(boneIdx);

                // Apply the offset's rotation then translation (preserve multiplication order from the C++ code)
                var offset = Transform;
                var combinedRot = Quaternion.Normalize(offset.Rotation * result.Rotation);
                result = result with { Rotation = combinedRot, Position = result.Position + offset.Position };

                var parentBoneIdx = skeleton.ParentIndices[boneIdx];
                if (parentBoneIdx != -1)
                {
                    // Compose local (bone) transform with parent's model-space transform
                    result *= pose.GetModelSpaceTransform(parentBoneIdx).ToMatrix();
                }
            }
            else // Get the model space transform for the target bone
            {
                result = pose.GetModelSpaceTransform(boneIdx);
                var offset = Transform;
                var combinedRot = Quaternion.Normalize(offset.Rotation * result.Rotation);
                result = result with { Rotation = combinedRot, Position = result.Position + offset.Position };
            }
        }
        else
        {
            result = pose.GetModelSpaceTransform(boneIdx);
        }

        return true;
    }
}
