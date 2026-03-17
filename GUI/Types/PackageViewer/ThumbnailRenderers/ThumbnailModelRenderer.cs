using System.Diagnostics;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailModelRenderer : ThumbnailRenderer
{
    public override void SetResource(Resource resource)
    {
        var model = (Model)resource.DataBlock!;

        Debug.Assert(SceneRenderer != null);

        SceneRenderer.Scene.Clear();

        var modelSceneNode = new ModelSceneNode(SceneRenderer.Scene, model);
        SceneRenderer.Scene.Add(modelSceneNode, true);

        var bbox = modelSceneNode.BoundingBox;

        var center = bbox.Center;
        // add some padding
        var size = bbox.Size * 1.5f;
        var biggestSide = Math.Max(size.X, Math.Max(size.Y, size.Z));

        SceneRenderer.Camera.RecalculateDirectionVectors();
        SceneRenderer.Camera.FrameObject(bbox.Center, size.X, size.Z, size.Y);
    }
};
