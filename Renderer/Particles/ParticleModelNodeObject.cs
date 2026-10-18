using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Particles;

/// <summary>
/// A model scene node as the object a particle control point is bound to: the model functions read
/// its current animated pose, its transform and the sequence it plays.
/// </summary>
internal sealed class ParticleModelNodeObject(Model model, ModelSceneNode node, IFileLoader fileLoader) : IParticleModelObject
{
    /// <inheritdoc/>
    public Model Model => model;

    /// <inheritdoc/>
    public Matrix4x4 Transform => node.Transform;

    /// <inheritdoc/>
    public void GetBonePose(Span<Matrix4x4> bones)
    {
        var pose = node.AnimationController.Pose;

        if (pose.Length < bones.Length)
        {
            BindPoseModelObject.GetBindPose(model, bones);
            return;
        }

        pose.AsSpan(0, bones.Length).CopyTo(bones);
    }

    /// <inheritdoc/>
    public int SequenceIndex => node.AnimationController.ActiveAnimation is { } animation
        ? ModelElements.FindSequence(model, animation.Name, fileLoader)
        : -1;

    /// <inheritdoc/>
    public float SequenceRate
    {
        get
        {
            var controller = node.AnimationController;

            if (controller.ActiveAnimation == null)
            {
                return 1f;
            }

            return controller.IsPaused ? 0f : controller.FrametimeMultiplier;
        }
    }
}
