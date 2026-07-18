namespace ValveResourceFormat.ResourceTypes.ModelAnimation;

/// <summary>
/// Computes world-space bone transforms from decoded animation frames.
/// </summary>
public static class FramePose
{
    /// <summary>
    /// Accumulates each bone's world-space transform from the frame's local bone transforms,
    /// walking down the skeleton hierarchy from the roots. <paramref name="world"/> must be at
    /// least as long as the skeleton's bone array and is indexed by bone index.
    /// </summary>
    public static void ComputeWorldPose(Frame frame, Skeleton skeleton, Span<Matrix4x4> world)
    {
        foreach (var root in skeleton.Roots)
        {
            ComputeWorldRecursive(root, Matrix4x4.Identity, frame, world);
        }
    }

    private static void ComputeWorldRecursive(Bone bone, Matrix4x4 parentWorld, Frame frame, Span<Matrix4x4> world)
    {
        var frameBone = frame.Bones[bone.Index];
        var local = Matrix4x4.CreateScale(frameBone.Scale)
            * Matrix4x4.CreateFromQuaternion(frameBone.Angle)
            * Matrix4x4.CreateTranslation(frameBone.Position);
        world[bone.Index] = local * parentWorld;

        foreach (var child in bone.Children)
        {
            ComputeWorldRecursive(child, world[bone.Index], frame, world);
        }
    }
}
