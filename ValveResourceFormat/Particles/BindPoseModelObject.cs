using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Particles;

/// <summary>
/// A model standing still in its bind pose, for a control point that carries a model with nothing
/// animating it.
/// </summary>
/// <param name="model">The model.</param>
public sealed class BindPoseModelObject(Model model) : IParticleModelObject
{
    /// <inheritdoc/>
    public Model Model { get; } = model;

    /// <inheritdoc/>
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

    /// <summary>Writes the bind pose of every bone of <paramref name="model"/>'s skeleton, in model space.</summary>
    /// <param name="model">The model whose skeleton to pose.</param>
    /// <param name="bones">Receives one transform per bone, indexed by bone index.</param>
    public static void GetBindPose(Model model, Span<Matrix4x4> bones)
    {
        foreach (var root in model.Skeleton.Roots)
        {
            Skeleton.ComputeWorldSubtree(root, Matrix4x4.Identity, null, bones);
        }
    }

    /// <summary>The inverse of the bind pose of every bone of <paramref name="model"/>'s skeleton, in model space.</summary>
    /// <param name="model">The model whose skeleton to pose.</param>
    /// <returns>One transform per bone, indexed by bone index.</returns>
    public static Matrix4x4[] GetInverseBindPose(Model model)
    {
        var bones = new Matrix4x4[model.Skeleton.Bones.Length];
        GetBindPose(model, bones);

        for (var i = 0; i < bones.Length; i++)
        {
            Matrix4x4.Invert(bones[i], out bones[i]);
        }

        return bones;
    }
}
