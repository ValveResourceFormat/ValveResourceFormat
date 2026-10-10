using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib;

partial struct Target
{
    /// <summary>Creates a set, non-bone target from a world/character-space transform.</summary>
    public Target(Transform transform)
    {
        Transform = transform;
        BoneID = default;
        IsBoneTarget = false;
        IsUsingBoneSpaceOffsets = false;
        HasOffsets = false;
        IsSet = true;
    }

    /// <summary>Creates a set target that follows a bone, moved by an offset in model space.</summary>
    public Target(GlobalSymbol boneID, Vector3 modelSpaceOffset)
    {
        BoneID = boneID;
        IsBoneTarget = true;
        IsSet = true;
        SetOffsets(Quaternion.Identity, modelSpaceOffset, isBoneSpaceOffset: false);
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
                var combinedRot = Quaternion.Normalize(result.Angle * offset.Angle);
                result = result with { Angle = combinedRot, Position = result.Position + offset.Position };

                var parentBoneIdx = skeleton.ParentIndices[boneIdx];
                if (parentBoneIdx != -1)
                {
                    // Compose local (bone) transform with parent's model-space transform
                    result *= pose.GetModelSpaceTransform(parentBoneIdx);
                }
            }
            else // Get the model space transform for the target bone
            {
                result = pose.GetModelSpaceTransform(boneIdx);
                var offset = Transform;
                var combinedRot = Quaternion.Normalize(result.Angle * offset.Angle);
                result = result with { Angle = combinedRot, Position = result.Position + offset.Position };
            }
        }
        else
        {
            result = pose.GetModelSpaceTransform(boneIdx);
        }

        return true;
    }
}
