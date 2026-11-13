using System.Collections.Frozen;
using System.Linq;
using ValveResourceFormat.Renderer.Editor.Collision;
using ValveResourceFormat.Renderer.SceneNodes;

namespace ValveResourceFormat.Renderer.Editor;

/// <summary>
/// Which tools-only content is drawn: entities that only exist in the editor, tools materials such as triggers
/// and clips, and collision. It decides each draw of a tools material the way Hammer decides each material of a mesh.
/// </summary>
public sealed class ToolsVisibility : IDrawFilter
{
    private readonly HashSet<SceneNode> selectedNodes = [];

    /// <summary>
    /// Gets or sets whether tools-only content is drawn: entities that only exist in the editor and tools
    /// materials. Off, only the content the game shows is drawn.
    /// </summary>
    public bool ShowTools { get; set; } = true;

    /// <summary>
    /// Gets or sets the hidden groups of tools materials, see <see cref="ToolBrushGroups"/>. A hidden group stays
    /// hidden whatever else shows tools materials. A group with no entity class is hidden whole, and one with a class
    /// only on the brushes of entities of that class, see <see cref="ToolBrushGroups.GetEntityClass"/>. The set is
    /// replaced whole, so a frame being drawn never reads it half changed.
    /// </summary>
    public IReadOnlySet<(string Group, string? EntityClass)> HiddenToolBrushGroups { get; set; } = FrozenSet<(string, string?)>.Empty;

    /// <summary>
    /// Gets or sets the collision labels that are drawn, see <see cref="PhysSceneNode.CollisionLabels"/>.
    /// Collision is drawn while any of its labels is. The set is replaced whole, so a frame being drawn never reads it
    /// half changed.
    /// </summary>
    public IReadOnlySet<string> ShownCollision { get; set; } = FrozenSet<string>.Empty;

    /// <summary>
    /// Gets or sets whether collision and the shapes of tools materials are drawn opaque, rather than as translucent
    /// fills and edge lines.
    /// </summary>
    public bool OpaqueCollision { get; set; }

    /// <summary>
    /// Shows the collision nodes that <see cref="ShownCollision"/> names, and draws them and the shapes of tools
    /// materials opaque or translucent as <see cref="OpaqueCollision"/> says.
    /// </summary>
    /// <param name="nodes">The nodes, of which only collision and the shapes of tools materials are changed.</param>
    public void ApplyCollisionVisibility(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes.OfType<ShapeSceneNode>())
        {
            if (node is PhysSceneNode { ToolsMaterial: null } collision)
            {
                collision.Enabled = collision.CollisionLabels.Any(ShownCollision.Contains);
            }
            else if (node.ToolsMaterial == null)
            {
                continue;
            }

            node.IsTranslucentRenderMode = !OpaqueCollision;
        }
    }

    /// <summary>Gets the distinct collision labels of the nodes, sorted.</summary>
    /// <param name="nodes">The nodes, of which only collision that shows no tools material is read.</param>
    /// <returns>The labels, see <see cref="PhysSceneNode.CollisionLabels"/>.</returns>
    public static string[] GetCollisionLabels(IEnumerable<SceneNode> nodes)
    {
        return [.. nodes
            .OfType<PhysSceneNode>()
            .Where(static node => node.ToolsMaterial == null)
            .SelectMany(static node => node.CollisionLabels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// The chosen layers to enable. Tools-only layers are left out while <see cref="ShowTools"/> is off, see
    /// <see cref="WorldLayer.IsToolsOnly"/>.
    /// </summary>
    /// <param name="chosenLayers">The layers chosen to be shown.</param>
    public HashSet<WorldLayer> FilterLayers(IEnumerable<WorldLayer> chosenLayers)
    {
        return ShowTools
            ? [.. chosenLayers]
            : [.. chosenLayers.Where(static layer => !layer.IsToolsOnly)];
    }

    /// <summary>Sets the selected nodes. They draw their tools materials even while those are hidden.</summary>
    /// <param name="nodes">The selected nodes.</param>
    public void SetSelectedNodes(IReadOnlyList<SceneNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        selectedNodes.Clear();

        for (var i = 0; i < nodes.Count; i++)
        {
            selectedNodes.Add(nodes[i]);
        }
    }

    /// <inheritdoc/>
    public DrawVisibility GetVisibility(SceneNode node, RenderMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);

        var hiddenGroups = HiddenToolBrushGroups;
        var hiddenByGroup = hiddenGroups.Count > 0
            && (ToolBrushGroups.GetGroup(material.Material.Name) ?? (material.IsToolsMaterial ? ToolBrushGroups.RootGroup : null)) is { } group
            && (hiddenGroups.Contains((group, null)) || hiddenGroups.Contains((group, ToolBrushGroups.GetEntityClass(node))));

        if ((!hiddenByGroup && (!material.IsToolsMaterial || ShowTools)) || selectedNodes.Contains(node))
        {
            return DrawVisibility.Drawn;
        }

        // A hidden group hides its materials entirely, but hidden tools materials that block light still cast shadows
        return !hiddenByGroup && material.BlocksLight ? DrawVisibility.ShadowOnly : DrawVisibility.Hidden;
    }
}
