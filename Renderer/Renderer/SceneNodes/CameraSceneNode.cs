using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneNodes;

/// <summary>A model marking a camera, which fades out as the view comes close so it does not block it.</summary>
public class CameraSceneNode : ModelSceneNode
{
    /// <summary>Initializes the marker from the camera model.</summary>
    /// <param name="scene">The scene the node belongs to.</param>
    /// <param name="model">The camera model.</param>
    public CameraSceneNode(Scene scene, Model model)
        : base(scene, model, null, true)
    {
    }

    /// <inheritdoc/>
    public override void Update(Scene.UpdateContext context)
    {
        base.Update(context);

        const float FadeOutStartDistance = 15f;
        var distanceFromCamera = Vector3.Distance(Transform.Translation, context.Camera.Location);
        var fadeOutCloseUp = MathUtils.Saturate(MathUtils.Remap(distanceFromCamera, 0f, FadeOutStartDistance));

        Alpha = fadeOutCloseUp;
    }
}
