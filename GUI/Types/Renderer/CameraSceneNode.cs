using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer;

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
        var distanceFromCamera = Vector3.Distance(Transform.Translation, context.View.Camera.Location);
        var fadeOutCloseUp = MathUtils.Saturate(MathUtils.Remap(distanceFromCamera, 0, FadeOutStartDistance));

        foreach (var mesh in RenderableMeshes)
        {
            mesh.Alpha = fadeOutCloseUp;
        }
    }
}
