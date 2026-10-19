using System.Collections.Frozen;
using System.Linq;
using ValveResourceFormat.Renderer.Editor.Collision;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Map;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.Editor;

/// <summary>
/// Which tools-only content is drawn: entities that only exist in the editor, tools materials such as triggers
/// and clips, and collision. It decides each draw of a tools material the way Hammer decides each material of a mesh.
/// </summary>
public sealed class ToolsVisibility : IDrawFilter
{
    // What only the editor draws, and the entities a template spawns, which are not in the world until it does
    private static readonly FrozenSet<string> ToolEntityLayerNames =
        FrozenSet.Create(StringComparer.Ordinal, HammerEntityVisuals.MarkerLayerName, HammerEntityVisuals.ConnectionsLayerName,
            HammerEntityVisuals.HelpersLayerName, WorldLoader.TemplateLayerName);

    private readonly HashSet<SceneNode> selectedNodes = [];

    /// <summary>
    /// Gets or sets whether entities that only exist in the editor are drawn. Off, only what the game shows is
    /// drawn, which hides tools materials too.
    /// </summary>
    public bool ShowToolEntities { get; set; } = true;

    /// <summary>Gets or sets whether tools materials are drawn.</summary>
    public bool ShowToolMaterials { get; set; }

    /// <summary>
    /// Gets or sets the groups of tools materials hidden, see <see cref="ToolBrushGroups"/>, which hide whatever else
    /// shows tools materials. It is replaced whole, so a frame being drawn never reads it half changed.
    /// </summary>
    public IReadOnlySet<string> HiddenToolBrushGroups { get; set; } = FrozenSet<string>.Empty;

    /// <summary>
    /// Gets or sets the collision drawn, by what it collides as, see <see cref="PhysSceneNode.CollisionLabel"/>. It is
    /// replaced whole, so a frame being drawn never reads it half changed.
    /// </summary>
    public IReadOnlySet<string> ShownCollision { get; set; } = FrozenSet<string>.Empty;

    /// <summary>
    /// Gets or sets whether collision and the shapes of tools materials are drawn opaque, rather than as translucent
    /// fills and edge lines.
    /// </summary>
    public bool OpaqueCollision { get; set; }

    /// <summary>
    /// Shows the collision among some nodes that <see cref="ShownCollision"/> names, and draws it and the shapes of
    /// tools materials as <see cref="OpaqueCollision"/> says.
    /// </summary>
    /// <param name="nodes">The nodes, of which only collision and the shapes of tools materials are changed.</param>
    public void ApplyCollisionVisibility(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes.OfType<ShapeSceneNode>())
        {
            if (node is PhysSceneNode { ToolsMaterial: null } collision)
            {
                collision.Enabled = ShownCollision.Contains(collision.CollisionLabel);
            }
            else if (node.ToolsMaterial == null)
            {
                continue;
            }

            node.IsTranslucentRenderMode = !OpaqueCollision;
        }
    }

    /// <summary>Gets what the collision among some nodes collides as, once each, in order.</summary>
    /// <param name="nodes">The nodes, of which only collision that shows no tools material is read.</param>
    /// <returns>The labels, see <see cref="PhysSceneNode.CollisionLabel"/>.</returns>
    public static string[] GetCollisionLabels(IEnumerable<SceneNode> nodes)
    {
        return [.. nodes
            .OfType<PhysSceneNode>()
            .Where(static node => node.ToolsMaterial == null)
            .Select(static node => node.CollisionLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Gets whether a layer only holds editor-only entities.</summary>
    /// <param name="layerName">The layer.</param>
    public static bool IsToolEntityLayer(string layerName) => ToolEntityLayerNames.Contains(layerName);

    /// <summary>The layers to enable out of those chosen, without the editor-only ones while they are hidden.</summary>
    /// <param name="chosenLayers">The layers chosen to be shown.</param>
    public HashSet<string> FilterLayers(IEnumerable<string> chosenLayers)
    {
        return ShowToolEntities
            ? [.. chosenLayers]
            : [.. chosenLayers.Where(static layer => !IsToolEntityLayer(layer))];
    }

    /// <summary>Sets the nodes that are selected, which draw their tools materials while those are hidden.</summary>
    /// <param name="nodes">The selected nodes.</param>
    public void SetSelectedNodes(IEnumerable<SceneNode> nodes)
    {
        selectedNodes.Clear();
        selectedNodes.UnionWith(nodes);
    }

    /// <inheritdoc/>
    public DrawVisibility GetVisibility(SceneNode node, RenderMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        var hiddenGroups = HiddenToolBrushGroups;
        var hiddenByGroup = hiddenGroups.Count > 0
            && (ToolBrushGroups.GetGroup(material.Material.Name) ?? (material.IsToolsMaterial ? ToolBrushGroups.RootGroup : null)) is { } group
            && hiddenGroups.Contains(group);

        // A shape shows its faces, see ToolsMaterialSceneNode
        if (node is ShadowCasterSceneNode)
        {
            return hiddenByGroup ? DrawVisibility.Hidden : DrawVisibility.ShadowOnly;
        }

        if ((!hiddenByGroup && (!material.IsToolsMaterial || (ShowToolMaterials && ShowToolEntities))) || selectedNodes.Contains(node))
        {
            return DrawVisibility.Drawn;
        }

        // A hidden group hides its materials entirely, while hidden tools materials that block light still do
        return !hiddenByGroup && material.BlocksLight ? DrawVisibility.ShadowOnly : DrawVisibility.Hidden;
    }
}
