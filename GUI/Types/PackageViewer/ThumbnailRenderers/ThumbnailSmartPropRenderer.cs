using System.Diagnostics;
using System.Drawing;
using System.Threading;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace GUI.Types.PackageViewer.ThumbnailRenderers;

internal class ThumbnailSmartPropRenderer : ThumbnailRenderer
{
    private const float FallbackExtent = 128f;
    private const int MaxElements = 512;
    private const int MaxModels = 256;
    private const int MaxNestedResources = 128;
    private const int MaxPathInstances = 512;

    private VrfGuiContext? guiContext;
    private readonly HashSet<Resource> loadedResources = [];
    private int loadedNestedResources;

    public override Bitmap? Render(PackageEntry entry, VrfGuiContext context, ThumbnailSizes thumbnailSize, CancellationToken cancellationToken)
    {
        guiContext = context;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return base.Render(entry, context, thumbnailSize, cancellationToken);
        }
        finally
        {
            foreach (var resource in loadedResources)
            {
                resource.Dispose();
            }

            loadedResources.Clear();
            loadedNestedResources = 0;
            guiContext = null;
        }
    }

    public override void SetResource(Resource resource, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SmartPropEvaluationResult result;
        try
        {
            var smartProp = (SmartProp)resource.DataBlock!;
            result = SmartPropEvaluator.Evaluate(
                smartProp.Data.Root,
                nestedPropResolver: LoadNestedSmartProp,
                maxElements: MaxElements,
                maxModels: MaxModels,
                maxPathInstances: MaxPathInstances);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(nameof(ThumbnailSmartPropRenderer), $"Failed to evaluate smart prop: {ex.Message}");
            FrameScene();
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var model in result.Models)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (model.ModelName.Length == 0 || !IsRenderableTransform(model.WorldMatrix))
            {
                continue;
            }

            try
            {
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
                    Flags = ObjectTypeFlags.NoShadows,
                };

                if (!IsFinite(modelSceneNode.BoundingBox.Min) || !IsFinite(modelSceneNode.BoundingBox.Max))
                {
                    modelSceneNode.Delete();
                    continue;
                }

                SceneRenderer.Scene.Add(modelSceneNode, false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(nameof(ThumbnailSmartPropRenderer), $"Failed to load model '{model.ModelName}': {ex.Message}");
            }
        }

        FrameScene();
    }

    private KVObject? LoadNestedSmartProp(string path)
    {
        if (loadedNestedResources >= MaxNestedResources)
        {
            return null;
        }

        try
        {
            var nested = guiContext?.LoadFileCompiled(path);
            if (nested?.DataBlock is SmartProp nestedProp)
            {
                loadedResources.Add(nested);
                loadedNestedResources++;
                return nestedProp.Data.Root;
            }

            nested?.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(nameof(ThumbnailSmartPropRenderer), $"Failed to load nested smart prop '{path}': {ex.Message}");
            return null;
        }
    }

    private static bool IsRenderableTransform(Matrix4x4 transform)
    {
        if (!float.IsFinite(transform.M11) || !float.IsFinite(transform.M12) || !float.IsFinite(transform.M13) || !float.IsFinite(transform.M14)
            || !float.IsFinite(transform.M21) || !float.IsFinite(transform.M22) || !float.IsFinite(transform.M23) || !float.IsFinite(transform.M24)
            || !float.IsFinite(transform.M31) || !float.IsFinite(transform.M32) || !float.IsFinite(transform.M33) || !float.IsFinite(transform.M34)
            || !float.IsFinite(transform.M41) || !float.IsFinite(transform.M42) || !float.IsFinite(transform.M43) || !float.IsFinite(transform.M44))
        {
            return false;
        }

        var determinant = transform.GetDeterminant();
        return float.IsFinite(determinant) && MathF.Abs(determinant) > 1e-8f;
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

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
