using System.Linq;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;

namespace ValveResourceFormat.Editor;

/// <summary>
/// Which tools-only content is drawn: entities that only exist in the editor, and tools materials such as
/// triggers and clips. A master switch hides both at once, and is kept per mode, shown in the viewer and
/// hidden in game so the world looks as the player sees it.
/// </summary>
public sealed class ToolsVisibility
{
    private readonly bool[] showToolsByMode = [true, false];

    /// <summary>Gets the mode whose master switch <see cref="ShowTools"/> reads and writes.</summary>
    public EditorMode Mode { get; internal set; }

    /// <summary>Gets or sets the master switch for all tools-only content, in the current mode.</summary>
    public bool ShowTools
    {
        get => showToolsByMode[(int)Mode];
        set => showToolsByMode[(int)Mode] = value;
    }

    /// <summary>Gets or sets whether entities that only exist in the editor are drawn, under the master switch.</summary>
    public bool ShowToolEntities { get; set; } = true;

    /// <summary>Gets or sets whether tools materials are drawn, under the master switch.</summary>
    public bool ShowToolMaterials { get; set; } = true;

    /// <summary>Gets whether editor-only entities end up drawn.</summary>
    public bool ToolEntitiesVisible => ShowTools && ShowToolEntities;

    /// <summary>Gets whether tools materials end up drawn.</summary>
    public bool ToolMaterialsVisible => ShowTools && ShowToolMaterials;

    /// <summary>Gets whether a layer only holds editor-only entities.</summary>
    /// <param name="layerName">The layer.</param>
    public static bool IsToolEntityLayer(string layerName) => WorldLoader.ToolEntityLayerNames.Contains(layerName);

    /// <summary>
    /// Gets whether collision shapes end up drawn, given whether their group was chosen to be shown. Tools
    /// materials only survive compilation as collision, so these are what the tools materials switch hides.
    /// </summary>
    /// <param name="node">The collision shapes.</param>
    /// <param name="groupChosen">Whether the group of the shapes was chosen to be shown.</param>
    public bool IsPhysicsVisible(PhysSceneNode node, bool groupChosen) => groupChosen && (!node.IsToolsMaterial || ToolMaterialsVisible);

    /// <summary>The layers to enable out of those chosen, without the editor-only ones while they are hidden.</summary>
    /// <param name="chosenLayers">The layers chosen to be shown.</param>
    public HashSet<string> FilterLayers(IEnumerable<string> chosenLayers)
    {
        return ToolEntitiesVisible
            ? [.. chosenLayers]
            : [.. chosenLayers.Where(static layer => !IsToolEntityLayer(layer))];
    }
}
