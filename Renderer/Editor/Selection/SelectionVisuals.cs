using System.Linq;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Selection;

/// <summary>Which lighting a selected node uses is shown linked to it.</summary>
public enum LightingBindingDisplay
{
    /// <summary>None.</summary>
    None,

    /// <summary>The environment maps the node is shaded with.</summary>
    EnvMaps,

    /// <summary>The light probe volume the node is bound to.</summary>
    LightProbe,
}

/// <summary>
/// Draws what is selected in a <see cref="SelectionSet"/> the way Hammer does: the shapes the helpers of a
/// selected entity show while it is, in place of the ones they show while it is not, and one box around what is
/// selected with the length of its edges, along with the name of each node and optionally the lighting it uses.
/// The selected nodes are outlined, and show their tools materials even while those are hidden.
/// </summary>
public sealed class SelectionVisuals
{
    private static readonly Vector2 NodeNameOffset = new(0, -20);
    private static readonly Color32 LightingBindingColor = new(1.0f, 0.0f, 1.0f, 1.0f);

    private readonly SelectionSet selection;
    private readonly List<SceneEnvMap> envMaps = [];
    private readonly HammerEntityVisuals entityVisuals;
    private readonly ToolsVisibility tools;
    private readonly HashSet<IHelperOwner> selectedEntities = [];
    private readonly HashSet<IHelperOwner> previouslySelectedEntities = [];
    private readonly Dictionary<IHelperOwner, Matrix4x4> pathsOfSelectedNodes = [];
    private readonly SelectionOutline outline;

    /// <summary>Gets the shapes of the selection, in world space, as the last <see cref="Update"/> drew them.</summary>
    public ToolGeometry Shapes { get; } = new();

    /// <summary>Gets or sets which lighting is shown linked to each selected node.</summary>
    public LightingBindingDisplay LightingBindings { get; set; }

    /// <summary>Initializes the visuals of a selection.</summary>
    /// <param name="selection">The selection to draw.</param>
    /// <param name="entityVisuals">The editor entities of the view, whose helpers draw the shapes of a selected entity.</param>
    /// <param name="tools">What tools-only content the view draws, told what is selected.</param>
    /// <param name="rendererContext">Renderer context for loading the outline shaders.</param>
    public SelectionVisuals(SelectionSet selection, HammerEntityVisuals entityVisuals, ToolsVisibility tools, RendererContext rendererContext)
    {
        ArgumentNullException.ThrowIfNull(rendererContext);

        this.selection = selection;
        this.entityVisuals = entityVisuals;
        this.tools = tools;
        outline = new SelectionOutline(rendererContext);
    }

    /// <summary>Draws the shapes and labels of the current selection again.</summary>
    /// <param name="renderContext">Render context providing the camera.</param>
    /// <param name="updateContext">Update context providing the text renderer.</param>
    public void Update(Scene.RenderContext renderContext, Scene.UpdateContext updateContext)
    {
        Shapes.Clear();

        // The entities selected last time are found again as the nodes are drawn
        previouslySelectedEntities.Clear();
        previouslySelectedEntities.UnionWith(selectedEntities);
        selectedEntities.Clear();
        pathsOfSelectedNodes.Clear();

        var context = new ToolRenderContext(Shapes);
        var nodes = selection.Nodes;
        AABB? bounds = null;

        tools.SetSelectedNodes(nodes);

        // What is selected is counted by the entities and other objects it is, not by the nodes drawing them
        var objectCount = nodes.Select(node => (object?)FindEntity(node) ?? node).Distinct().Count();

        foreach (var node in nodes)
        {
            if (DrawNode(context, renderContext.Camera, updateContext.TextRenderer, node, isOnlySelected: objectCount == 1) is { } nodeBounds)
            {
                bounds = bounds?.Union(nodeBounds) ?? nodeBounds;
            }
        }

        // A path is drawn as selected while any of its nodes is
        foreach (var (path, toWorld) in pathsOfSelectedNodes)
        {
            if (!selectedEntities.Contains(path))
            {
                DrawPath(context, path, renderContext.Camera, toWorld);
            }
        }

        // One box around everything selected
        if (bounds is { } selectionBounds)
        {
            SelectionBounds.Draw(context, Matrix4x4.Identity, selectionBounds, renderContext.Camera, updateContext.TextRenderer);
        }

        // The entities no longer selected show the shapes they show while they are not
        foreach (var entity in previouslySelectedEntities)
        {
            if (!selectedEntities.Contains(entity))
            {
                SetSelected(entity, isSelected: false);
            }
        }
    }

    /// <summary>Outlines the selected nodes as the last frame drew them.</summary>
    /// <param name="renderer">The renderer of the frame, which resolved the scene depth.</param>
    /// <param name="sceneFramebuffer">What the scene was rendered into.</param>
    /// <param name="target">What the outline is drawn over, the size of the scene.</param>
    public void RenderOutline(Renderer renderer, Framebuffer sceneFramebuffer, Framebuffer target)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(sceneFramebuffer);
        ArgumentNullException.ThrowIfNull(target);

        outline.Render(renderer, sceneFramebuffer, target, selection.Nodes);
    }

    /// <summary>Deletes the GL objects.</summary>
    public void Delete()
    {
        Shapes.Delete();
        outline.Delete();
    }

    private static void SetSelected(IHelperOwner entity, bool isSelected)
    {
        foreach (var helper in entity.Helpers)
        {
            helper.SetSelected(isSelected);
        }
    }

    private IHelperOwner? FindEntity(SceneNode node) => node.EntityData != null ? entityVisuals.FindEntity(node) : null;

    // Gives the bounds the node adds to those of the selection, in the world
    private AABB? DrawNode(ToolRenderContext context, Camera camera, TextRenderer textRenderer, SceneNode node, bool isOnlySelected)
    {
        var nodeName = node.Name ?? node.GetType().Name;

        // Drawn with the main camera, so 3D sky nodes are outlined where they appear in the world
        var toWorld = node.Scene.ToViewerWorld;
        var bounds = node.BoundingBox.Transform(toWorld);
        AABB? selectionBounds = node.IsPointMarker ? null : bounds;

        if (node.EntityData?.GetStringProperty("classname") is { } classname)
        {
            nodeName = classname;
        }

        // An entity adds its bounds once, however many of the nodes it is drawn with are selected
        if (FindEntity(node) is { } entity)
        {
            selectionBounds = null;

            if (selectedEntities.Add(entity))
            {
                DrawEntity(context, entity, camera, toWorld, isOnlySelected);
                selectionBounds = GetObjectBounds(entity).Transform(toWorld);
            }
        }

        DrawLightingBindings(context, node, toWorld, bounds.Center);

        // draw node name above the bounding box
        var position = bounds.Center;
        position.Z = bounds.Max.Z;

        textRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
        {
            Scale = 14f,
            Text = nodeName,
            CenterHorizontal = true,
            TextOffset = NodeNameOffset
        }, camera);

        // An icon is a marker, so the size it is drawn at is not part of what is selected
        return selectionBounds;
    }

    /// <summary>What the helpers of an entity cover and what it is made of besides, or its origin where that is nothing.</summary>
    internal static AABB GetObjectBounds(IHelperOwner entity)
    {
        var bounds = entity.ContentBounds;

        foreach (var helper in entity.Helpers)
        {
            if (helper.ObjectBounds is { } helperBounds)
            {
                bounds = bounds?.Union(helperBounds) ?? helperBounds;
            }
        }

        return bounds ?? new AABB(entity.Transform.Translation, entity.Transform.Translation);
    }

    // The shapes the helpers of a selected entity draw, in place of the ones they draw while it is not selected
    private void DrawEntity(ToolRenderContext context, IHelperOwner entity, Camera camera, in Matrix4x4 toWorld, bool isOnlySelected)
    {
        if (!previouslySelectedEntities.Contains(entity))
        {
            SetSelected(entity, isSelected: true);
        }

        if (entity.Path is { } path)
        {
            pathsOfSelectedNodes.TryAdd(path, toWorld);
        }

        var state = new HelperRenderState(IsSelected: true, IsOnlySelected: isOnlySelected) { Camera = camera };

        context.PushAbsoluteTransform(toWorld);

        // Selecting an entity shows every helper it has, whichever layers hide them while it is not
        foreach (var helper in entity.Helpers)
        {
            helper.Render(context, state);
        }

        context.PopTransform();
    }

    // The helpers of a path that are drawn as selected while a node of it is
    private static void DrawPath(ToolRenderContext context, IHelperOwner path, Camera camera, in Matrix4x4 toWorld)
    {
        var state = new HelperRenderState(IsSelected: true, IsOnlySelected: false) { Camera = camera };

        context.PushAbsoluteTransform(toWorld);

        foreach (var helper in path.Helpers)
        {
            if (helper.IsSelectedWithPathNodes)
            {
                helper.Render(context, state);
            }
        }

        context.PopTransform();
    }

    private void DrawLightingBindings(ToolRenderContext context, SceneNode node, in Matrix4x4 toWorld, Vector3 nodeCenter)
    {
        switch (LightingBindings)
        {
            case LightingBindingDisplay.EnvMaps:
                envMaps.Clear();
                node.Scene.GetEnvMapsUsedBy(node, envMaps);

                foreach (var envMap in envMaps)
                {
                    DrawLink(context, envMap, toWorld, nodeCenter);
                }

                break;

            case LightingBindingDisplay.LightProbe when node.LightProbeBinding is { } probe:
                DrawLink(context, probe, toWorld, nodeCenter);
                break;
        }
    }

    /// <summary>Outlines a lighting volume and connects it to the node that uses it, over the scene.</summary>
    private static void DrawLink(ToolRenderContext context, SceneNode source, in Matrix4x4 toWorld, Vector3 nodeCenter)
    {
        var transform = source.Transform * toWorld;

        context.PushRenderMode(ToolRenderMode.WireframeNoZ);
        context.DrawColor = LightingBindingColor;

        context.PushTransform(transform);
        context.DrawWireBox(source.LocalBoundingBox.Min, source.LocalBoundingBox.Max);
        context.PopTransform();

        context.DrawLine(transform.Translation, nodeCenter);
        context.PopRenderMode();
    }
}
