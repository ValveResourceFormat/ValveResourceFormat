using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.Renderer.PickingTexture;

namespace GUI.Types.GLViewers;

internal sealed class GLVmapViewer : GLSingleNodeViewer
{
    private readonly Datamodel.Element mapRoot;
    private readonly List<Resource> loadedResources = [];
    private readonly Dictionary<int, EntityLump.Entity> entitiesByNodeId;
    private bool attemptedContentGameSearchPaths;

    public Action<EntityLump.Entity>? ShowEntityInList { get; set; }

    protected override bool ShowToolsMaterialsByDefault => false;

    public GLVmapViewer(
        VrfGuiContext guiContext,
        RendererContext rendererContext,
        Datamodel.Element mapRoot,
        IReadOnlyList<ValveMapEntity> entities)
        : base(guiContext, rendererContext)
    {
        this.mapRoot = mapRoot;
        entitiesByNodeId = entities.ToDictionary(item => item.NodeId, item => item.Entity);
    }

    protected override void LoadScene()
    {
        base.LoadScene();

        TryLoadContentGameSearchPaths();
        InitializeSoundPlayer();
        var sourceMapWorld = WorldLoader.LoadSourceMapEntities(
            GuiContext.FileName,
            Scene,
            entitiesByNodeId.Values.ToList());

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
            LoadChildren(world, Matrix4x4.Identity, null);
        }
    }

    private void LoadChildren(Datamodel.Element parent, Matrix4x4 parentTransform, EntityLump.Entity? ownerEntity)
    {
        if (!parent.TryGetValue("children", out var childrenValue) || childrenValue is not Datamodel.ElementArray children)
        {
            return;
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is Datamodel.Element child)
            {
                LoadElement(child, parentTransform, ownerEntity);
            }
        }
    }

    private void LoadElement(Datamodel.Element element, Matrix4x4 parentTransform, EntityLump.Entity? ownerEntity)
    {
        var worldTransform = ReadTransform(element) * parentTransform;
        var tint = ReadTint(element);
        var nodeId = ReadInt32(element, "nodeID");
        entitiesByNodeId.TryGetValue(nodeId, out var entity);
        entity ??= ownerEntity;

        if (ValveMapMeshReader.TryRead(element, out var mapMesh) && mapMesh != null)
        {
            var node = new ValveMapMeshSceneNode(Scene, mapMesh)
            {
                Transform = mapMesh.Transform * worldTransform,
                EntityData = entity,
            };

            if (tint.HasValue)
            {
                node.Tint = tint.Value;
            }

            Scene.Add(node, false);
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

        LoadChildren(element, worldTransform, entity);
    }

    private bool AddModel(
        string modelName,
        Matrix4x4 transform,
        string? materialGroup,
        Vector4? tint,
        EntityLump.Entity? entity = null)
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
        if (UiControl?.Parent is TabPage tabPage && tabPage.Parent is TabControl tabControl)
        {
            tabControl.SelectTab(tabPage);
        }

        var nodes = Scene.AllNodes.Where(node => ReferenceEquals(node.EntityData, entity)).ToList();
        var bounds = nodes.Count > 0
            ? nodes.Skip(1).Aggregate(nodes[0].BoundingBox, static (current, node) => current.Union(node.BoundingBox))
            : default;
        var center = nodes.Count > 0 ? bounds.Center : entity.GetVector3Property("origin");
        var extent = nodes.Count > 0 ? MathF.Max(64f, bounds.Size.Length()) : 64f;

        if (nodes.Count > 0)
        {
            SelectEntityNodes(entity, toggle: false);
        }

        Input.SaveCameraForTransition();
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

    protected override void OnPicked(object? sender, PickingTexture.PickingResponse pickingResponse)
    {
        var selectedNodeRenderer = SelectedNodeRenderer;
        Debug.Assert(selectedNodeRenderer != null);

        var pixelInfo = pickingResponse.PixelInfo;
        if (pixelInfo.ObjectId == 0 || pixelInfo.Unused2 != 0)
        {
            selectedNodeRenderer.SelectNode(null);
            return;
        }

        var sceneNode = Scene.Find(pixelInfo.ObjectId);
        if (sceneNode == null)
        {
            return;
        }

        if (pickingResponse.Intent == PickingIntent.Select)
        {
            if (sceneNode.EntityData != null)
            {
                SelectEntityNodes(sceneNode.EntityData, Control.ModifierKeys.HasFlag(Keys.Control));
            }
            else if (Control.ModifierKeys.HasFlag(Keys.Control))
            {
                selectedNodeRenderer.ToggleNode(sceneNode);
            }
            else
            {
                selectedNodeRenderer.SelectNode(sceneNode);
            }

            return;
        }

        if (pickingResponse.Intent == PickingIntent.Details && sceneNode.EntityData != null)
        {
            SelectEntityNodes(sceneNode.EntityData, toggle: false);
            Program.MainForm.Invoke(() => ShowEntityInList?.Invoke(sceneNode.EntityData));
        }
    }

    private void SelectEntityNodes(EntityLump.Entity entity, bool toggle)
    {
        var selectedNodeRenderer = SelectedNodeRenderer;
        Debug.Assert(selectedNodeRenderer != null);

        var nodes = Scene.AllNodes.Where(node => ReferenceEquals(node.EntityData, entity)).ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        if (!toggle)
        {
            selectedNodeRenderer.SelectNode(nodes[0], forceDisableDepth: true);
            foreach (var node in nodes.Skip(1))
            {
                selectedNodeRenderer.ToggleNode(node);
            }

            return;
        }

        var select = nodes.Any(static node => !node.IsSelected);
        foreach (var node in nodes.Where(node => node.IsSelected != select))
        {
            selectedNodeRenderer.ToggleNode(node);
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
    }
}
