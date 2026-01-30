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

    public readonly bool TryGetTransform(/*Pose pose, */out Transform result)
    {
        if (!IsBoneTarget)
        {
            result = Transform; // Just use the internal transform
            return true;
        }

        /*
        EE_ASSERT( pPose != nullptr );
        auto pSkeleton = pPose->GetSkeleton();

        int32_t const boneIdx = pSkeleton->GetBoneIndex( m_boneID );
        if ( boneIdx == InvalidIndex )
        {
            return false;
        }

        if ( m_hasOffsets )
        {
            // Get the local transform and the parent global transform
            if ( m_isUsingBoneSpaceOffsets )
            {
                outTransform = pPose->GetTransform( boneIdx );
                outTransform.SetRotation( m_transform.GetRotation() * outTransform.GetRotation() );
                outTransform.SetTranslation( outTransform.GetTranslation() + m_transform.GetTranslation() );

                int32_t const parentBoneIdx = pSkeleton->GetParentBoneIndex( boneIdx );
                if ( parentBoneIdx != InvalidIndex )
                {
                    outTransform = outTransform * pPose->GetModelSpaceTransform( parentBoneIdx );
                }
            }
            else // Get the model space transform for the target bone
            {
                outTransform = pPose->GetModelSpaceTransform( boneIdx );
                outTransform.SetRotation( m_transform.GetRotation() * outTransform.GetRotation() );
                outTransform.SetTranslation( outTransform.GetTranslation() + m_transform.GetTranslation() );
            }
        }
        else
        {
            outTransform = pPose->GetModelSpaceTransform( boneIdx );
        }
        */
        return true;
    }
}
