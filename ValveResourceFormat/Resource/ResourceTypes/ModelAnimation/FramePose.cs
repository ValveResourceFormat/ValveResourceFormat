namespace ValveResourceFormat.ResourceTypes.ModelAnimation;

/// <summary>
/// Computes world-space bone transforms from decoded animation frames or the bind pose.
/// </summary>
public static class FramePose
{
    /// <summary>
    /// Accumulates world-space transforms for one bone subtree under the given parent transform.
    /// A <see langword="null"/> <paramref name="frame"/> yields the bind pose.
    /// </summary>
    public static void ComputeWorldSubtree(Bone bone, Matrix4x4 parentWorld, Frame? frame, Span<Matrix4x4> world)
    {
        var local = bone.BindPose;

        if (frame != null)
        {
            var frameBone = frame.Bones[bone.Index];
            local = Matrix4x4.CreateScale(frameBone.Scale)
                * Matrix4x4.CreateFromQuaternion(frameBone.Angle)
                * Matrix4x4.CreateTranslation(frameBone.Position);
        }

        world[bone.Index] = local * parentWorld;

        foreach (var child in bone.Children)
        {
            ComputeWorldSubtree(child, world[bone.Index], frame, world);
        }
    }
}
