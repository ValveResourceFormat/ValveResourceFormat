using ValveResourceFormat;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailParticleRenderer : ThumbnailRenderer
{
    public override void SetResource(Resource resource)
    {
        var particleSystem = (ParticleSystem)resource.DataBlock!;

        var particleSceneNode = new ParticleSceneNode(SceneRenderer!.Scene, particleSystem);
        SceneRenderer.Scene.Add(particleSceneNode, true);

        // Update once with 100ms to give particles a chance to simulate/emit/start rendering
        var updateContext = new ValveResourceFormat.Renderer.Scene.UpdateContext
        {
            Camera = SceneRenderer.Camera,
            TextRenderer = null!,
            Timestep = 0.1f,
        };

        SceneRenderer.Scene.Update(updateContext);

        var bbox = particleSceneNode.BoundingBox;

        // Add some padding
        var size = bbox.Size * 1.5f;

        SceneRenderer.Camera.FrameObject(bbox.Center, size.X, size.Z, size.Y);
    }
}
