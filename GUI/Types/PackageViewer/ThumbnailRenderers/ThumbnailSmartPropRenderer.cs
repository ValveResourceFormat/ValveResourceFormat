using System.Diagnostics;
using System.Drawing;
using System.Threading;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailSmartPropRenderer : ThumbnailRenderer
{
    private const float FallbackExtent = 128f;

    private VrfGuiContext? guiContext;
    private readonly List<Resource> loadedResources = [];

    public override Bitmap? Render(PackageEntry entry, VrfGuiContext context, ThumbnailSizes thumbnailSize, CancellationToken cancellationToken)
    {
        guiContext = context;
        try
        {
            return base.Render(entry, context, thumbnailSize, cancellationToken);
        }
        finally
        {
            foreach (var resource in loadedResources)
            {
                resource.Dispose();
            }

            loadedResources.Clear();
            guiContext = null;
        }
    }

    public override void SetResource(Resource resource)
    {
        var smartProp = (SmartProp)resource.DataBlock!;
        var result = SmartPropEvaluator.Evaluate(
            smartProp.Data.Root,
            nestedPropResolver: LoadNestedSmartProp);

        foreach (var model in result.Models)
        {
            if (model.ModelName.Length == 0)
            {
                continue;
            }

            var modelResource = guiContext?.LoadFileCompiled(model.ModelName);
            if (modelResource?.DataBlock is not Model modelBlock)
            {
                modelResource?.Dispose();
                continue;
            }

            loadedResources.Add(modelResource);

            var modelSceneNode = new ModelSceneNode(SceneRenderer!.Scene, modelBlock, isWorldPreview: true)
            {
                Transform = model.WorldMatrix,
            };
            SceneRenderer.Scene.Add(modelSceneNode, false);
        }

        FrameScene();
    }

    private KVObject? LoadNestedSmartProp(string path)
    {
        var nested = guiContext?.LoadFileCompiled(path);
        if (nested?.DataBlock is SmartProp nestedProp)
        {
            loadedResources.Add(nested);
            return nestedProp.Data.Root;
        }

        nested?.Dispose();
        return null;
    }

    private void FrameScene()
    {
        Debug.Assert(SceneRenderer != null);

        var bounds = default(AABB);
        var first = true;
        foreach (var node in SceneRenderer.Scene.AllNodes)
        {
            if (first)
            {
                bounds = node.BoundingBox;
                first = false;
                continue;
            }

            bounds = bounds.Union(node.BoundingBox);
        }

        if (first || bounds.Size.LengthSquared() < 1e-4f)
        {
            // Nothing renderable: frame a default volume so the thumbnail stays stable
            SceneRenderer.Camera.FrameObject(Vector3.Zero, FallbackExtent, FallbackExtent, FallbackExtent);
            return;
        }

        var size = bounds.Size * 1.5f;
        SceneRenderer.Camera.FrameObject(bounds.Center, size.X, size.Z, size.Y);
    }
}
