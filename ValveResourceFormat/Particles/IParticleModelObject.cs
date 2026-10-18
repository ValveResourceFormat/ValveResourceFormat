using ValveResourceFormat.ResourceTypes;

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
    void GetBonePose(Span<Matrix4x4> bones) => BindPoseModelObject.GetBindPose(Model, bones);

    /// <summary>The index of the sequence the object plays among the model's sequences, or -1 when it plays none.</summary>
    int SequenceIndex => -1;

    /// <summary>How fast the object plays <see cref="SequenceIndex"/>, 1 at its authored speed.</summary>
    float SequenceRate => 1f;
}
