using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer.Particles;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailSnapshotRenderer : ThumbnailRenderer
{
    private const float PointScreenSize = 0.02f;

    public override void SetResource(Resource resource)
    {
        if (resource.GetBlockByType(BlockType.SNAP) is not ParticleSnapshot snapshot || !SnapshotParticleSystem.CanPreview(snapshot))
        {
            return;
        }

        var particleSystem = SnapshotParticleSystem.Create(snapshot);

        var particleSceneNode = new ParticleSceneNode(SceneRenderer!.Scene, particleSystem, snapshot, true);
        particleSceneNode.SetTextureOverride(SceneRenderer.Scene.RendererContext.MaterialLoader.GetDefaultColor());
        SnapshotParticleSystem.SetScreenSize(particleSceneNode, PointScreenSize, SceneRenderer.Camera.GetFOV());

        SceneRenderer.Scene.Add(particleSceneNode, true);

        // Update once with 100ms to give the burst emitter a chance to place the particles
        var updateContext = new ValveResourceFormat.Renderer.Scene.UpdateContext
        {
            Camera = SceneRenderer.Camera,
            TextRenderer = null!,
            Timestep = 0.1f,
        };

        SceneRenderer.Scene.Update(updateContext);

        // Framed off the snapshot rather than the node, whose box is padded by the system's bounding box.
        var bbox = SnapshotParticleSystem.GetBounds(snapshot);

        // Add some padding
        var size = bbox.Size * 1.5f;

        SceneRenderer.Camera.FrameObject(bbox.Center, size.X, size.Y, size.Z);
    }
}
