using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Particles;

/// <summary>
/// The object a control point is bound to when it carries a model, such as the entity an effect is
/// attached to: where it stands, how its skeleton is posed and which sequence it plays. The model
/// functions place particles on its hitboxes and bones, skin snapshots to it and read its scale.
/// </summary>
public interface IParticleModelObject
{
    /// <summary>The model the object shows.</summary>
    Model Model { get; }

    /// <summary>Carries the model's space into the world, scale included.</summary>
    Matrix4x4 Transform { get; }

    /// <summary>
    /// Writes every bone of <see cref="Model"/>'s skeleton in its current pose, in model space and
    /// indexed by bone index. <paramref name="bones"/> is as long as the skeleton.
    /// </summary>
    /// <param name="bones">Receives one transform per bone.</param>
    void GetBonePose(Span<Matrix4x4> bones) => ParticleModelObject.GetBindPose(Model, bones);

    /// <summary>
    /// The index of the sequence the object plays among the model's sequences, or -1 when it plays
    /// none, and how fast it plays it.
    /// </summary>
    (int Sequence, float Rate) Sequence => (-1, 1f);
}

/// <summary>
/// A model standing still in its bind pose, for a control point that carries a model with nothing
/// animating it.
/// </summary>
/// <param name="model">The model.</param>
public class ParticleModelObject(Model model) : IParticleModelObject
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
}
