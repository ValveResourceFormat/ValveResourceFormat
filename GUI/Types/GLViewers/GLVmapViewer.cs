using System.IO;
using System.Linq;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace GUI.Types.GLViewers;

internal sealed class GLVmapViewer : GLSingleNodeViewer
{
    private readonly Datamodel.Element mapRoot;
    private readonly List<Resource> loadedResources = [];
    private readonly Dictionary<string, KVObject?> compiledSmartProps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, EntityLump.Entity> entitiesByNodeId;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<SmartPropMapPart>> savedSmartPropParts;
    private bool attemptedContentGameSearchPaths;

    public GLVmapViewer(
        VrfGuiContext guiContext,
        RendererContext rendererContext,
        Datamodel.Element mapRoot,
        IReadOnlyList<ValveMapEntity> entities)
        : base(guiContext, rendererContext)
    {
        this.mapRoot = mapRoot;
        entitiesByNodeId = entities.ToDictionary(item => item.NodeId, item => item.Entity);
        savedSmartPropParts = SmartPropMapPartSet.ReadAll(mapRoot);
    }

    protected override void LoadScene()
    {
        base.LoadScene();

        TryLoadContentGameSearchPaths();
        InitializeSoundPlayer();
        var sourceMapWorld = WorldLoader.LoadSourceMapEntities(
            GuiContext.FileName,
            Scene,
            entitiesByNodeId.Values.Where(static entity => entity.GetStringProperty("classname") != "CMapSmartProp").ToList());

        if (sourceMapWorld.SkyboxScene != null)
        {
            Renderer.SkyboxScene = sourceMapWorld.SkyboxScene;
        }

        if (sourceMapWorld.Skybox2D != null)
        {
            Renderer.Skybox2D = sourceMapWorld.Skybox2D;
        }

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
        var nodeId = ReadInt32(element, "nodeID");
        entitiesByNodeId.TryGetValue(nodeId, out var entity);

        if (smartProp != null)
        {
            if (!LoadSavedSmartProp(nodeId, worldTransform, tint, entity)
                && !LoadSmartProp(smartProp, worldTransform, tint, entity))
            {
                AddEntityMarker(worldTransform, new Color32(0, 180, 255, 255), smartProp.SmartPropFilename, entity);
            }
        }
        else if (entity == null && TryGetModelName(element, out var modelName))
        {
            if (!AddModel(modelName, worldTransform, null, tint, entity))
            {
                AddEntityMarker(worldTransform, new Color32(255, 80, 180, 255), modelName, entity);
            }
        }
        else if (entity == null && TryGetEntityClass(element, out var className))
        {
            AddEntityMarker(worldTransform, new Color32(255, 0, 255, 255), className, entity);
        }

        LoadChildren(element, worldTransform);
    }

    private bool LoadSavedSmartProp(
        int nodeId,
        Matrix4x4 placementTransform,
        Vector4? placementTint,
        EntityLump.Entity? entity)
    {
        if (!savedSmartPropParts.TryGetValue(nodeId, out var parts))
        {
            return false;
        }

        var loadedAny = false;
        foreach (var part in parts)
        {
            var tint = part.TintColor.HasValue && placementTint.HasValue
                ? part.TintColor.Value * placementTint.Value
                : part.TintColor ?? placementTint;
            loadedAny |= AddModel(
                part.ModelName,
                part.Transform * placementTransform,
                null,
                tint,
                entity,
                part.Deformer,
                part.Transform);
        }

        return loadedAny;
    }

    private bool LoadSmartProp(
        SmartPropMapParameters parameters,
        Matrix4x4 placementTransform,
        Vector4? placementTint,
        EntityLump.Entity? entity)
    {
        var smartPropRoot = ResolveCompiledSmartProp(parameters.SmartPropFilename);
        if (smartPropRoot == null)
        {
            return false;
        }

        var context = parameters.CreateEvaluationContext(smartPropRoot);
        var result = SmartPropEvaluator.Evaluate(smartPropRoot, context, ResolveCompiledSmartProp);
        var loadedAny = false;
        foreach (var model in result.Models)
        {
            var tint = model.TintColor.HasValue && placementTint.HasValue
                ? model.TintColor.Value * placementTint.Value
                : model.TintColor ?? placementTint;
            loadedAny |= AddModel(model.ModelName, model.WorldMatrix * placementTransform, model.MaterialGroup, tint, entity);
        }

        return loadedAny;
    }

    private bool AddModel(
        string modelName,
        Matrix4x4 transform,
        string? materialGroup,
        Vector4? tint,
        EntityLump.Entity? entity = null,
        SmartPropMapDeformer? deformer = null,
        Matrix4x4 deformerPartTransform = default)
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
        Func<ValveResourceFormat.Blocks.VBIB, ValveResourceFormat.Blocks.VBIB>? meshBufferTransform = deformer == null
            ? null
            : vbib => SmartPropMeshDeformer.Deform(vbib, deformerPartTransform, deformer);
        var node = new ModelSceneNode(Scene, model, skin: materialGroup, meshBufferTransform: meshBufferTransform)
        {
            Transform = transform,
            EntityData = entity,
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
        var resource = LoadCompiledResource(modelName);
        return resource?.DataBlock is Model ? resource : null;
    }

    private Resource? LoadCompiledResource(string filename)
    {
        var resource = GuiContext.LoadFileCompiled(filename);
        if (resource != null)
        {
            return resource;
        }

        foreach (var path in GetLocalCompiledCandidates(filename))
        {
            resource = GuiContext.LoadFile(path);
            if (resource != null)
            {
                return resource;
            }
        }

        if (TryLoadContentGameSearchPaths())
        {
            return GuiContext.LoadFileCompiled(filename);
        }

        return null;
    }

    private bool TryLoadContentGameSearchPaths()
    {
        if (attemptedContentGameSearchPaths)
        {
            return false;
        }

        attemptedContentGameSearchPaths = true;
        var mapPath = Path.GetFullPath(GuiContext.FileName);
        var contentMarker = $"{Path.DirectorySeparatorChar}content{Path.DirectorySeparatorChar}";
        var contentIndex = mapPath.IndexOf(contentMarker, StringComparison.OrdinalIgnoreCase);
        if (contentIndex < 0)
        {
            return false;
        }

        var installRoot = mapPath[..contentIndex];
        var relativeMapPath = mapPath[(contentIndex + contentMarker.Length)..];
        var parts = relativeMapPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var assetFolder = parts[0];
        const string AddonsSuffix = "_addons";
        var gameFolder = assetFolder.EndsWith(AddonsSuffix, StringComparison.OrdinalIgnoreCase)
            ? assetFolder[..^AddonsSuffix.Length]
            : assetFolder;
        var gameInfoPath = Path.Combine(installRoot, "game", gameFolder, "gameinfo.gi");
        if (!File.Exists(gameInfoPath))
        {
            return false;
        }

        var modIdentifierPath = gameInfoPath;
        if (assetFolder.EndsWith(AddonsSuffix, StringComparison.OrdinalIgnoreCase) && parts.Length > 1)
        {
            var addonGamePath = Path.Combine(installRoot, "game", assetFolder, parts[1]);
            if (Directory.Exists(addonGamePath))
            {
                GuiContext.AddDiskPathToSearch(addonGamePath);
            }

            var addonInfoPath = Path.Combine(addonGamePath, "addoninfo.txt");
            if (File.Exists(addonInfoPath))
            {
                modIdentifierPath = addonInfoPath;
            }
        }

        GuiContext.FindAndLoadSearchPaths(modIdentifierPath);
        return true;
    }

    private IEnumerable<string> GetLocalCompiledCandidates(string filename)
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
        var normalizedFilename = filename.Replace('/', Path.DirectorySeparatorChar);
        var compiledName = string.Concat(normalizedFilename, ValveResourceFormat.IO.GameFileLoader.CompiledFileSuffix);

        yield return Path.Combine(installRoot, "game", relativeAssetRoot, compiledName);
        yield return Path.Combine(installRoot, "content", relativeAssetRoot, compiledName);
    }

    private void AddEntityMarker(
        Matrix4x4 transform,
        Color32 color,
        string name,
        EntityLump.Entity? entity)
    {
        var marker = new SimpleBoxSceneNode(Scene, color, new Vector3(8f))
        {
            Transform = transform,
            Name = name,
            EntityData = entity,
        };
        Scene.Add(marker, false);
    }

    private KVObject? ResolveCompiledSmartProp(string filename)
    {
        if (compiledSmartProps.TryGetValue(filename, out var cached))
        {
            return cached;
        }

        var resource = LoadCompiledResource(filename);
        var root = resource?.DataBlock is SmartProp smartProp ? smartProp.Data.Root : null;
        if (root != null && resource != null && !loadedResources.Contains(resource))
        {
            loadedResources.Add(resource);
        }

        compiledSmartProps[filename] = root;
        return root;
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

    private static int ReadInt32(Datamodel.Element element, string name)
        => element.TryGetValue(name, out var value) && value is IConvertible convertible
            ? convertible.ToInt32(null)
            : 0;

    public void SelectAndFocusEntity(EntityLump.Entity entity)
    {
        var node = Scene.Find(entity);
        var center = node?.BoundingBox.Center ?? entity.GetVector3Property("origin");
        var extent = node == null ? 64f : MathF.Max(64f, node.BoundingBox.Size.Length());
        Input.Camera.SetLocation(center + new Vector3(extent));
        Input.Camera.LookAt(center);
        NotifyVisible();
    }

    public void SelectAndFocusEntities(IReadOnlyList<EntityLump.Entity> entities)
    {
        if (entities.Count > 0)
        {
            SelectAndFocusEntity(entities[0]);
        }
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
        compiledSmartProps.Clear();
    }
}
