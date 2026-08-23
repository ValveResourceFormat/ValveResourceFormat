using System.IO;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace GUI.Types.GLViewers;

internal sealed class GLVmapViewer : GLSingleNodeViewer
{
    private readonly Datamodel.Element mapRoot;
    private readonly List<Resource> loadedResources = [];

    public GLVmapViewer(VrfGuiContext guiContext, RendererContext rendererContext, Datamodel.Element mapRoot)
        : base(guiContext, rendererContext)
    {
        this.mapRoot = mapRoot;
    }

    protected override void LoadScene()
    {
        base.LoadScene();

        if (mapRoot.TryGetValue("world", out var worldValue) && worldValue is Datamodel.Element world)
        {
            LoadChildren(world, Matrix4x4.Identity);
        }
    }

    private void LoadChildren(Datamodel.Element parent, Matrix4x4 parentTransform)
    {
        if (!parent.TryGetValue("children", out var childrenValue) || childrenValue is not Datamodel.ElementArray children)
        {
            return;
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is Datamodel.Element child)
            {
                LoadElement(child, parentTransform);
            }
        }
    }

    private void LoadElement(Datamodel.Element element, Matrix4x4 parentTransform)
    {
        var worldTransform = ReadTransform(element) * parentTransform;
        var tint = ReadTint(element);
        var smartProp = SmartPropMapParameters.Read(element);

        if (smartProp != null)
        {
            if (!LoadSmartProp(smartProp, worldTransform, tint))
            {
                AddEntityMarker(worldTransform, new Color32(0, 180, 255, 255), smartProp.SmartPropFilename);
            }
        }
        else if (TryGetModelName(element, out var modelName))
        {
            if (!AddModel(modelName, worldTransform, null, tint))
            {
                AddEntityMarker(worldTransform, new Color32(255, 80, 180, 255), modelName);
            }
        }
        else if (TryGetEntityClass(element, out var className))
        {
            AddEntityMarker(worldTransform, new Color32(255, 0, 255, 255), className);
        }

        LoadChildren(element, worldTransform);
    }

    private bool LoadSmartProp(SmartPropMapParameters parameters, Matrix4x4 placementTransform, Vector4? placementTint)
    {
        var smartPropRoot = ResolveSmartProp(parameters.SmartPropFilename);
        if (smartPropRoot == null)
        {
            return false;
        }

        var context = parameters.CreateEvaluationContext(smartPropRoot);
        var result = SmartPropEvaluator.Evaluate(smartPropRoot, context, ResolveSmartProp);
        var loadedAny = false;
        foreach (var model in result.Models)
        {
            var tint = model.TintColor.HasValue && placementTint.HasValue
                ? model.TintColor.Value * placementTint.Value
                : model.TintColor ?? placementTint;
            loadedAny |= AddModel(model.ModelName, model.WorldMatrix * placementTransform, model.MaterialGroup, tint);
        }

        return loadedAny;
    }

    private bool AddModel(string modelName, Matrix4x4 transform, string? materialGroup, Vector4? tint)
    {
        if (modelName.Length == 0)
        {
            return false;
        }

        var resource = LoadCompiledModel(modelName);
        if (resource?.DataBlock is not Model model)
        {
            resource?.Dispose();
            return false;
        }

        loadedResources.Add(resource);
        var node = new ModelSceneNode(Scene, model, skin: materialGroup)
        {
            Transform = transform,
        };

        if (tint.HasValue)
        {
            node.Tint = tint.Value;
        }

        Scene.Add(node, false);
        return true;
    }

    private Resource? LoadCompiledModel(string modelName)
    {
        var resource = GuiContext.LoadFileCompiled(modelName);
        if (resource != null)
        {
            return resource;
        }

        foreach (var path in GetLocalCompiledCandidates(modelName))
        {
            resource = GuiContext.LoadFile(path);
            if (resource != null)
            {
                return resource;
            }
        }

        return null;
    }

    private IEnumerable<string> GetLocalCompiledCandidates(string modelName)
    {
        var mapPath = Path.GetFullPath(GuiContext.FileName);
        var contentMarker = $"{Path.DirectorySeparatorChar}content{Path.DirectorySeparatorChar}";
        var contentIndex = mapPath.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
        if (contentIndex < 0)
        {
            yield break;
        }

        var installRoot = mapPath[..contentIndex];
        var relativeMapPath = mapPath[(contentIndex + contentMarker.Length)..];
        var parts = relativeMapPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            yield break;
        }

        var assetRootParts = parts[0].EndsWith("_addons", StringComparison.OrdinalIgnoreCase) && parts.Length > 1 ? 2 : 1;
        var relativeAssetRoot = Path.Combine(parts[..assetRootParts]);
        var normalizedModelName = modelName.Replace('/', Path.DirectorySeparatorChar);
        var compiledName = string.Concat(normalizedModelName, ValveResourceFormat.IO.GameFileLoader.CompiledFileSuffix);

        yield return Path.Combine(installRoot, "game", relativeAssetRoot, compiledName);
        yield return Path.Combine(installRoot, "content", relativeAssetRoot, compiledName);
    }

    private void AddEntityMarker(Matrix4x4 transform, Color32 color, string name)
    {
        var marker = new SimpleBoxSceneNode(Scene, color, new Vector3(8f))
        {
            Transform = transform,
            Name = name,
        };
        Scene.Add(marker, false);
    }

    private KVObject? ResolveSmartProp(string filename)
    {
        var normalizedFilename = filename.Replace('/', Path.DirectorySeparatorChar);
        var directory = Path.GetDirectoryName(GuiContext.FileName);
        var adjacentFallback = directory == null ? null : Path.Combine(directory, Path.GetFileName(normalizedFilename));

        while (!string.IsNullOrEmpty(directory))
        {
            var path = Path.Combine(directory, normalizedFilename);
            if (File.Exists(path))
            {
                return KVDocumentExtensions.ParseKV3(path).Root;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return adjacentFallback != null && File.Exists(adjacentFallback)
            ? KVDocumentExtensions.ParseKV3(adjacentFallback).Root
            : null;
    }

    private static Matrix4x4 ReadTransform(Datamodel.Element element)
    {
        var origin = ReadVector(element, "origin", Vector3.Zero);
        var scales = ReadVector(element, "scales", Vector3.One);
        var angles = Vector3.Zero;

        if (element.TryGetValue("angles", out var anglesValue) && anglesValue is Datamodel.QAngle qAngle)
        {
            angles = new Vector3(qAngle.Pitch, qAngle.Yaw, qAngle.Roll);
        }

        return Matrix4x4.CreateScale(scales)
            * EntityTransformHelper.EulerAnglesToRotationMatrix(angles)
            * Matrix4x4.CreateTranslation(origin);
    }

    private static Vector3 ReadVector(Datamodel.Element element, string name, Vector3 fallback)
        => element.TryGetValue(name, out var value) && value is Vector3 vector ? vector : fallback;

    private static Vector4? ReadTint(Datamodel.Element element)
    {
        if (!element.TryGetValue("tintColor", out var value) || value is not Datamodel.Color color)
        {
            return null;
        }

        return new Vector4(color.R, color.G, color.B, color.A) / byte.MaxValue;
    }

    private static bool TryGetModelName(Datamodel.Element element, out string modelName)
    {
        if (TryReadModelValue(element, "model", out modelName))
        {
            return true;
        }

        if (element.TryGetValue("entity_properties", out var propertiesValue)
            && propertiesValue is Datamodel.Element properties
            && TryReadModelValue(properties, "model", out modelName))
        {
            return true;
        }

        modelName = string.Empty;
        return false;
    }

    private static bool TryGetEntityClass(Datamodel.Element element, out string className)
    {
        if (element.TryGetValue("entity_properties", out var propertiesValue)
            && propertiesValue is Datamodel.Element properties
            && properties.TryGetValue("classname", out var classValue)
            && classValue is string text && text.Length > 0)
        {
            className = text;
            return true;
        }

        className = string.Empty;
        return false;
    }

    private static bool TryReadModelValue(Datamodel.Element element, string name, out string modelName)
    {
        if (element.TryGetValue(name, out var value) && value is string text
            && text.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
        {
            modelName = text;
            return true;
        }

        modelName = string.Empty;
        return false;
    }

    public override void Dispose()
    {
        base.Dispose();

        foreach (var resource in loadedResources)
        {
            resource.Dispose();
        }

        loadedResources.Clear();
    }
}
