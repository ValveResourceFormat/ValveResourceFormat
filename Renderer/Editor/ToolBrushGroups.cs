using System.Collections.Frozen;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor;

/// <summary>
/// The groups Hammer's Auto Vis Groups sort tools materials into under "Tool Brushes", as CS2 declares them with the
/// <c>toolsMaterial</c> filters of its FGD files, which hold the groups older Hammer builds have built in. A group
/// holds only the materials named for it, so a group below a hidden one can still be shown. The root group holds the
/// tools materials no group names. Unlike Hammer, which files the clips of each surface together under Clip, each has
/// a group of its own.
/// </summary>
public static class ToolBrushGroups
{
    /// <summary>The group every other group is below, which holds the tools materials no group names.</summary>
    public const string RootGroup = "Tool Brushes";

    private const string ToolsFolder = "materials/tools/";

    private static readonly FrozenDictionary<string, string> GroupByMaterial = new (string Group, string Material)[]
    {
        ("Clip/Clip", "toolsclip.vmat"),
        ("Clip/Player Clip", "toolsplayerclip.vmat"),
        ("Clip/NPC Clip", "toolsnpcclip.vmat"),
        ("Clip/Nav Clip", "toolsnavclip.vmat"),
        ("Clip/Teleport Clip", "toolsteleportclip.vmat"),
        ("Clip/Control Clip", "toolscontrolclip.vmat"),
        ("Nodraw", "toolsnodraw.vmat"),
        ("Skybox", "toolsskybox.vmat"),
        ("Trigger", "toolstrigger.vmat"),
        ("Block Light", "toolsblocklight.vmat"),
        ("Block Light", "toolssolidblocklight.vmat"),
        ("Lightmap Space", "toolslightmapres.vmat"),
        ("Vis Blocker", "visblocker.vmat"),
        ("Vis Blocker", "skyvisblocker.vmat"),
        ("Invisible", "toolsinvisible.vmat"),
        ("Ladder", "toolsinvisibleladder.vmat"),
        ("Markup Volume AI", "toolsmarkupvolumeai.vmat"),
        ("Skip", "toolsskip.vmat"),
        ("Occluder", "toolsoccluder.vmat"),
        ("Precipitation", "toolsprecipitation.vmat"),
        ("Block LOS", "toolsblock_los.vmat"),
        ("Clip/Bullets", "toolsblockbullets.vmat"),
        ("Clip/Nav Space Clip", "toolsnavspaceclip.vmat"),
        ("Steam Audio", "toolssteamaudiofullabsorption.vmat"),
        ("Steam Audio", "toolssteamaudiofulldiffuse.vmat"),
        ("Clip/Concrete", "toolsclip_concrete.vmat"),
        ("Clip/Plastic", "toolsclip_plastic.vmat"),
        ("Clip/Wood Basket", "toolsclip_wood_basket.vmat"),
        ("Clip/Gravel", "toolsclip_gravel.vmat"),
        ("Clip/Wood", "toolsclip_wood.vmat"),
        ("Clip/Metal", "toolsclip_metal.vmat"),
        ("Clip/Metal Vehicle", "toolsclip_metalvehicle.vmat"),
        ("Clip/Metal Sand Barrel", "toolsclip_metal_sand_barrel.vmat"),
        ("Clip/Wood Crate", "toolsclip_wood_crate.vmat"),
        ("Clip/Rubber Tire", "toolsclip_rubbertire.vmat"),
        ("Clip/Sand", "toolsclip_sand.vmat"),
        ("Clip/Dirt", "toolsclip_dirt.vmat"),
        ("Clip/Glass", "toolsclip_glass.vmat"),
        ("Clip/Metal Grate", "toolsclip_metalgrate.vmat"),
        ("Clip/Rubber", "toolsclip_rubber.vmat"),
        ("Place", "tools_cs_place.vmat"),
        ("Clip/Grenades", "toolsgrenadeclip.vmat"),
        ("Clip/Bullets", "toolsblockbullets_cs.vmat"),
        ("Ladder", "toolsinvisibleladder_wood.vmat"),
        ("Skybox", "toolsskybox_occluder.vmat"),
    }.ToFrozenDictionary(static filter => ToolsFolder + filter.Material, static filter => $"{RootGroup}/{filter.Group}", StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the group a material is in.</summary>
    /// <param name="materialName">The path of the material, such as <c>materials/tools/toolsclip.vmat</c>.</param>
    /// <returns>The group, or <see langword="null"/> for a material no group holds.</returns>
    public static string? GetGroup(string materialName) => GroupByMaterial.GetValueOrDefault(materialName);

    /// <summary>Gets the class of the entity a node draws the brushes of.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The class, <c>worldspawn</c> for brushes of no entity.</returns>
    public static string GetEntityClass(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.EntityData?.GetStringProperty("classname") ?? "worldspawn";
    }

    /// <summary>Gets a group and the groups its path continues, outermost last.</summary>
    /// <param name="group">The group, such as <c>Tool Brushes/Clip/Player Clip</c>.</param>
    public static IEnumerable<string> GetGroupAndParents(string group)
    {
        ArgumentNullException.ThrowIfNull(group);

        for (var end = group.Length; end > 0; end = group.LastIndexOf('/', end - 1))
        {
            yield return group[..end];
        }
    }
}
