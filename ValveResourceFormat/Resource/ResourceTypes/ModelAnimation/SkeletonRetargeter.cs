namespace ValveResourceFormat.ResourceTypes.ModelAnimation;

/// <summary>
/// Retargets animation posed on one skeleton onto another skeleton by bone name,
/// reproducing the source skeleton's world-space poses on the target hierarchy. Used for
/// animation graph (NM) clips, whose frames target a skeleton separate from the model's.
/// </summary>
public sealed class SkeletonRetargeter
{
    private readonly Skeleton modelSkeleton;

    // model bone index -> source bone index by name (-1 when the source does not drive that bone)
    private readonly int[] modelToSource;
    private readonly Matrix4x4[] sourceWorldScratch;

    /// <summary>
    /// Gets the source skeleton the retargeted animation is decoded on.
    /// </summary>
    public Skeleton SourceSkeleton { get; }

    /// <summary>
    /// Gets the model-to-source bone index map, indexed by model bone index; -1 marks model
    /// bones the source skeleton does not drive.
    /// </summary>
    public ReadOnlySpan<int> ModelToSource => modelToSource;

    /// <summary>
    /// Gets whether any model bone maps to a source bone by name; retargeting produces the bind
    /// pose when nothing maps.
    /// </summary>
    public bool HasMappedBones { get; }

    /// <summary>
    /// Builds the model-to-source bone map by bone name, once per skeleton pair.
    /// </summary>
    public SkeletonRetargeter(Skeleton modelSkeleton, Skeleton sourceSkeleton)
    {
        this.modelSkeleton = modelSkeleton;
        SourceSkeleton = sourceSkeleton;

        modelToSource = new int[modelSkeleton.Bones.Length];
        for (var m = 0; m < modelSkeleton.Bones.Length; m++)
        {
            var sourceBone = sourceSkeleton[modelSkeleton.Bones[m].Name];
            modelToSource[m] = sourceBone?.Index ?? -1;
            HasMappedBones |= sourceBone != null;
        }

        sourceWorldScratch = new Matrix4x4[sourceSkeleton.Bones.Length];
    }

    /// <summary>
    /// Reproduces the clip frame's world poses on the model skeleton: mapped model bones take
    /// their source bone's world pose, unmapped bones follow their parent at bind pose.
    /// <paramref name="modelWorld"/> receives the model skeleton's world transforms, indexed by
    /// bone index.
    /// </summary>
    public void Retarget(Frame clipFrame, Span<Matrix4x4> modelWorld)
    {
        FramePose.ComputeWorldPose(clipFrame, SourceSkeleton, sourceWorldScratch);

        foreach (var root in modelSkeleton.Roots)
        {
            RetargetSubtree(root, Matrix4x4.Identity, sourceWorldScratch, modelWorld);
        }
    }

    /// <summary>
    /// Retargets one model-skeleton subtree from precomputed source world poses: mapped model
    /// bones copy their source bone's world pose, unmapped bones follow their parent at bind
    /// pose. <paramref name="sourceWorld"/> is indexed by source bone index.
    /// </summary>
    public void RetargetSubtree(Bone bone, Matrix4x4 parentWorld, ReadOnlySpan<Matrix4x4> sourceWorld, Span<Matrix4x4> modelWorld)
    {
        var sourceIndex = modelToSource[bone.Index];
        modelWorld[bone.Index] = sourceIndex >= 0 ? sourceWorld[sourceIndex] : bone.BindPose * parentWorld;

        foreach (var child in bone.Children)
        {
            RetargetSubtree(child, modelWorld[bone.Index], sourceWorld, modelWorld);
        }
    }
}
