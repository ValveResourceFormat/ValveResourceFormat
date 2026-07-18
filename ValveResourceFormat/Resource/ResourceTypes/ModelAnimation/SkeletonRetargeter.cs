namespace ValveResourceFormat.ResourceTypes.ModelAnimation;

/// <summary>
/// Retargets animation frames decoded on one skeleton onto another skeleton by bone name,
/// reproducing the source skeleton's world-space poses on the target hierarchy. Used for
/// animation graph (NM) clips, whose frames target a skeleton separate from the model's.
/// </summary>
public sealed class SkeletonRetargeter
{
    private readonly Skeleton modelSkeleton;
    private readonly Skeleton clipSkeleton;

    // model bone index -> clip bone index by name (-1 when the clip does not drive that bone)
    private readonly int[] modelToClip;
    private readonly Matrix4x4[] clipWorld;

    /// <summary>
    /// Gets whether any model bone maps to a clip bone by name; retargeting produces the bind
    /// pose when nothing maps.
    /// </summary>
    public bool HasMappedBones { get; }

    /// <summary>
    /// Builds the model-to-clip bone map by bone name, once per skeleton pair.
    /// </summary>
    public SkeletonRetargeter(Skeleton modelSkeleton, Skeleton clipSkeleton)
    {
        this.modelSkeleton = modelSkeleton;
        this.clipSkeleton = clipSkeleton;

        modelToClip = new int[modelSkeleton.Bones.Length];
        for (var m = 0; m < modelSkeleton.Bones.Length; m++)
        {
            var clipBone = clipSkeleton[modelSkeleton.Bones[m].Name];
            modelToClip[m] = clipBone?.Index ?? -1;
            HasMappedBones |= clipBone != null;
        }

        clipWorld = new Matrix4x4[clipSkeleton.Bones.Length];
    }

    /// <summary>
    /// Reproduces the clip frame's world poses on the model skeleton: mapped model bones take
    /// their clip bone's world pose, unmapped bones follow their parent at bind pose.
    /// <paramref name="modelWorld"/> receives the model skeleton's world transforms, indexed by
    /// bone index.
    /// </summary>
    public void Retarget(Frame clipFrame, Span<Matrix4x4> modelWorld)
    {
        FramePose.ComputeWorldPose(clipFrame, clipSkeleton, clipWorld);

        foreach (var root in modelSkeleton.Roots)
        {
            RetargetRecursive(root, Matrix4x4.Identity, modelWorld);
        }
    }

    private void RetargetRecursive(Bone bone, Matrix4x4 parentWorld, Span<Matrix4x4> modelWorld)
    {
        var clipIndex = modelToClip[bone.Index];
        modelWorld[bone.Index] = clipIndex >= 0 ? clipWorld[clipIndex] : bone.BindPose * parentWorld;

        foreach (var child in bone.Children)
        {
            RetargetRecursive(child, modelWorld[bone.Index], modelWorld);
        }
    }
}
