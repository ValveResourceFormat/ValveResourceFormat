using System.Diagnostics;
using System.Linq;
using ValveResourceFormat;
using ValveResourceFormat.Renderer.Editor.Collision;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailModelRenderer : ThumbnailRenderer
{
    public override void SetResource(Resource resource)
    {
        var model = (Model)resource.DataBlock!;
        PhysAggregateData? phys = null;

        Debug.Assert(SceneRenderer != null);

        var modelSceneNode = new ModelSceneNode(SceneRenderer.Scene, model, isWorldPreview: true);
        SceneRenderer.Scene.Add(modelSceneNode, true);

        // if model has no meshes try to show physics
        if (modelSceneNode == null || modelSceneNode.RenderableMeshes.Count == 0)
        {
            phys = model.GetEmbeddedPhys();

            if (phys != null)
            {
                foreach (var node in PhysSceneNode.Create(SceneRenderer.Scene, phys, null))
                {
                    node.Enabled = true;
                    node.IsTranslucentRenderMode = false;
                    SceneRenderer.Scene.Add(node, false);
                }
            }
        }

        var bbox = SceneRenderer.Scene.AllNodes
        .Select(n => n.BoundingBox)
        .Aggregate((a, b) => a.Union(b));

        // add some padding
        var size = bbox.Size * (phys == null ? 1.5f : 1);

        SceneRenderer.Camera.FrameObject(bbox.Center, size.X, size.Z, size.Y);
    }
}
