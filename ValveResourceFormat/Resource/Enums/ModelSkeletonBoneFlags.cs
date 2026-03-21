namespace ValveResourceFormat
{
    /// <summary>
    /// Flags describing how a bone is used within a model skeleton.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/ModelSkeletonData_t::BoneFlags_t">ModelSkeletonData_t::BoneFlags_t</seealso>
    [Flags]
    public enum ModelSkeletonBoneFlags
    {
        /// <summary>No bone flags set.</summary>
        NoBoneFlags = 0x0,

        /// <summary>Bone is used by a flex driver.</summary>
        BoneFlexDriver = 0x4,

        /// <summary>Bone is used by cloth simulation.</summary>
        Cloth = 0x8,

        /// <summary>Bone is used by physics.</summary>
        Physics = 0x10,

        /// <summary>Bone is used by an attachment point.</summary>
        Attachment = 0x20,

        /// <summary>Bone is used by animation.</summary>
        Animation = 0x40,

        /// <summary>Bone is used by a mesh.</summary>
        Mesh = 0x80,

        /// <summary>Bone is used by a hitbox.</summary>
        Hitbox = 0x100,

        /// <summary>Bone is used as a retarget source.</summary>
        RetargetSrc = 0x200,

        /// <summary>Bone is used by vertex data at LOD level 0.</summary>
        BoneUsedByVertexLod0 = 0x400,

        /// <summary>Bone is used by vertex data at LOD level 1.</summary>
        BoneUsedByVertexLod1 = 0x800,

        /// <summary>Bone is used by vertex data at LOD level 2.</summary>
        BoneUsedByVertexLod2 = 0x1000,

        /// <summary>Bone is used by vertex data at LOD level 3.</summary>
        BoneUsedByVertexLod3 = 0x2000,

        /// <summary>Bone is used by vertex data at LOD level 4.</summary>
        BoneUsedByVertexLod4 = 0x4000,

        /// <summary>Bone is used by vertex data at LOD level 5.</summary>
        BoneUsedByVertexLod5 = 0x8000,

        /// <summary>Bone is used by vertex data at LOD level 6.</summary>
        BoneUsedByVertexLod6 = 0x10000,

        /// <summary>Bone is used by vertex data at LOD level 7.</summary>
        BoneUsedByVertexLod7 = 0x20000,

        /// <summary>Bone participates in bone merge as a read source.</summary>
        BoneMergeRead = 0x40000,

        /// <summary>Bone participates in bone merge as a write target.</summary>
        BoneMergeWrite = 0x80000,

        /// <summary>Bone rotation is pre-aligned; no further alignment is needed during blending.</summary>
        BlendPrealigned = 0x100000,

        /// <summary>Bone has a rigid (fixed) length constraint.</summary>
        RigidLength = 0x200000,

        /// <summary>Bone is driven procedurally at runtime.</summary>
        Procedural = 0x400000,

        /// <summary>Bone is used by procedural cloth simulation. Added by VRF; not a native engine flag.</summary>
        ProceduralCloth = Cloth | Procedural,
    }
}
