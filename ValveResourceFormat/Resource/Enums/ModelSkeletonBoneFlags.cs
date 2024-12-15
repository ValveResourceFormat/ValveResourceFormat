namespace ValveResourceFormat
{
    [Flags]
    public enum ModelSkeletonBoneFlags
    {
        NoBoneFlags = 0x0,
        BoneFlexDriver = 0x4,
        Cloth = 0x8,
        Physics = 0x10,
        Attachment = 0x20,
        Animation = 0x40,
        Mesh = 0x80,
        Hitbox = 0x100,
        RetargetSrc = 0x200,
        BoneUsedByVertexLod0 = 0x400,
        BoneUsedByVertexLod1 = 0x800,
        BoneUsedByVertexLod2 = 0x1000,
        BoneUsedByVertexLod3 = 0x2000,
        BoneUsedByVertexLod4 = 0x4000,
        BoneUsedByVertexLod5 = 0x8000,
        BoneUsedByVertexLod6 = 0x10000,
        BoneUsedByVertexLod7 = 0x20000,
        BoneMergeRead = 0x40000,
        BoneMergeWrite = 0x80000,
        BlendPrealigned = 0x100000,
        RigidLength = 0x200000,
        Procedural = 0x400000,

        ProceduralCloth = Cloth | Procedural, // Added by VRF
    }
}
