using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A Panorama panel drawn in the world: <c>point_clientui_world_panel</c>, its text variant
/// <c>point_clientui_world_text_panel</c>, and HL:A's <c>point_clientui_world_movie_panel</c>.
/// The panel's contents are not rendered, only the rectangle they would fill.
/// </summary>
public sealed class PointClientUIWorldPanel : BaseEntity
{
    /// <summary>Initializes a world panel from its keyvalues.</summary>
    public PointClientUIWorldPanel(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    protected override SceneNode? CreateRootNode()
    {
        // The text panel's keys default to a caption centred above its origin that turns to face the player
        var isTextPanel = Classname.Equals("point_clientui_world_text_panel", StringComparison.OrdinalIgnoreCase);

        var size = new Vector2(
            KeyValues.GetFloatProperty("width", isTextPanel ? 128f : 32f),
            KeyValues.GetFloatProperty("height", isTextPanel ? 24f : 32f));

        var anchor = new Vector2(
            AlignToAnchor(KeyValues.GetInt32Property("horizontal_align", isTextPanel ? 1 : 0)),
            AlignToAnchor(KeyValues.GetInt32Property("vertical_align", 0)));

        var orientation = (WorldPanelSceneNode.PanelOrientation)KeyValues.GetInt32Property("orientation", isTextPanel ? 2 : 0);

        return new WorldPanelSceneNode(Scene, size, anchor, orientation, Color32.White);
    }

    // Left/Bottom, Center, Right/Top, as the fraction of the panel that lies before its origin
    private static float AlignToAnchor(int align) => align switch
    {
        1 => 0.5f,
        2 => 1f,
        _ => 0f,
    };
}
