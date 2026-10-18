using System.Collections.Frozen;
using System.Linq;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Renderer.Editor;

/// <summary>
/// Which tools-only content is drawn: entities that only exist in the editor, and tools materials such as
/// triggers and clips.
/// </summary>
public sealed class ToolsVisibility
{
    // What only the editor draws, and the entities a template spawns, which are not in the world until it does
    private static readonly FrozenSet<string> ToolEntityLayerNames =
        FrozenSet.Create(StringComparer.Ordinal, HammerEntityVisuals.MarkerLayerName, HammerEntityVisuals.ConnectionsLayerName, WorldLoader.TemplateLayerName);

    /// <summary>Gets or sets whether entities that only exist in the editor are drawn.</summary>
    public bool ShowToolEntities { get; set; } = true;

    /// <summary>Gets or sets whether tools materials are drawn. Off until asked for.</summary>
    public bool ShowToolMaterials { get; set; }

    /// <summary>Gets whether a layer only holds editor-only entities.</summary>
    /// <param name="layerName">The layer.</param>
    public static bool IsToolEntityLayer(string layerName) => ToolEntityLayerNames.Contains(layerName);

    /// <summary>
    /// Gets whether collision shapes end up drawn, given whether their group was chosen to be shown. Tools
    /// materials only survive compilation as collision, so these are what the tools materials switch hides.
    /// </summary>
    /// <param name="node">The collision shapes.</param>
    /// <param name="groupChosen">Whether the group of the shapes was chosen to be shown.</param>
    public bool IsPhysicsVisible(PhysSceneNode node, bool groupChosen)
    {
        ArgumentNullException.ThrowIfNull(node);

        return groupChosen && (!node.IsToolsMaterial || ShowToolMaterials);
    }

    /// <summary>The layers to enable out of those chosen, without the editor-only ones while they are hidden.</summary>
    /// <param name="chosenLayers">The layers chosen to be shown.</param>
    public HashSet<string> FilterLayers(IEnumerable<string> chosenLayers)
    {
        return ShowToolEntities
            ? [.. chosenLayers]
            : [.. chosenLayers.Where(static layer => !IsToolEntityLayer(layer))];
    }
}
