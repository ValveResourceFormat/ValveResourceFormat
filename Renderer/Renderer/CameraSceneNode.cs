using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer;

class CameraSceneNode : ModelSceneNode
{
    public CameraSceneNode(Scene scene, Model model)
        : base(scene, model, null, true)
    {
    }

    public override void Update(Scene.UpdateContext context)
    {
        base.Update(context);

        const float FadeOutStartDistance = 15f;
        var distanceFromCamera = Vector3.Distance(Transform.Translation, context.Camera.Location);
        var fadeOutCloseUp = MathUtils.Saturate(MathUtils.Remap(distanceFromCamera, 0, FadeOutStartDistance));

        foreach (var mesh in RenderableMeshes)
        {
            mesh.Alpha = fadeOutCloseUp;
        }
    }
}
