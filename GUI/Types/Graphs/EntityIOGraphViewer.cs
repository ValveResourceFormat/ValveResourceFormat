using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using Connection = ValveResourceFormat.ResourceTypes.EntityLump.Connection;

using ValveResourceFormat.Graphs;
namespace GUI.Types.Graphs;

/// <summary>
/// Graph viewer for the entity I/O system of an entity lump: entities as nodes,
/// output-to-input connections as labeled wires.
/// </summary>
internal class EntityIOGraphViewer : GLGraphViewer
{
    private const GraphHue OutputHue = EntityIOGraphBuilder.OutputHue;
    private const GraphHue InputHue = EntityIOGraphBuilder.InputHue;

    private readonly List<List<GraphNode>> islands;
    private string? focusedIslandName;
    private readonly Action<IReadOnlyList<EntityLump.Entity>>? showInMap;
    private readonly Dictionary<GraphNode, List<EntityLump.Entity>> nodeMembers = [];
    private readonly int entityCount;

    public EntityIOGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, EntityLump entityLump)
        : this(vrfGuiContext, rendererContext, CollectEntities(entityLump, rendererContext), showInMap: null)
    {
    }

    // A lump opened on its own still references entities of its child lumps (templates,
    // spawners), so resolve targets across the whole lump tree.
    private static List<EntityLump.Entity> CollectEntities(EntityLump entityLump, RendererContext rendererContext)
    {
        try
        {
            var entities = new List<EntityLump.Entity>();

            foreach (var traversed in EntityLumpTraversal.EnumerateEntities(entityLump, rendererContext.FileLoader, Matrix4x4.Identity))
            {
                entities.Add(traversed.Entity);
            }

            return entities;
        }
        catch (Exception e)
        {
            Log.Warn(nameof(EntityIOGraphViewer), $"Failed to traverse child entity lumps: {e.Message}");
            return entityLump.GetEntities();
        }
    }

    public EntityIOGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, List<EntityLump.Entity> entities, Action<IReadOnlyList<EntityLump.Entity>>? showInMap)
        : base(vrfGuiContext, rendererContext, new GraphView())
    {
        this.showInMap = showInMap;
        entityCount = entities.Count;
        EntityIOGraphBuilder.Build(View.Document, entities, nodeMembers,
            new Progress<string>(message => Log.Debug(nameof(EntityIOGraphViewer), message)));

        View.Legend.AddRange(
        [
            new("Output", OutputHue, GraphLegendKind.Wire),
            new("Input", InputHue, GraphLegendKind.Wire),
            new("point_template", GraphHue.Emerald),
            new("Template spawn", GraphHue.Purple, GraphLegendKind.DashedWire),
            new("Sound entity", GraphHue.Pink),
            new("Special target", GraphHue.Magenta),
            new("Unresolved target", GraphHue.Red),
        ]);

        islands = View.GetComponents();
        islands.Sort(static (a, b) => b.Count.CompareTo(a.Count));

        LoadEntityIcons();
        View.IconResolver = key => iconsByClassname.GetValueOrDefault(key);

        // Icons widen their nodes, so lay out once the node content is final.
        View.LayoutNodesPacked();
    }

    // Hammer editor icons: convention path materials/editor/<classname>.vmat, plus FGD-derived
    // aliases for classes whose icon material is named differently.
    private static readonly Dictionary<string, string> IconAliases = new()
    {
        ["filter_multi"] = "filter_multiple",
        ["filter_activator_name"] = "filter_name",
        ["filter_activator_context"] = "filter_name",
        ["filter_activator_class"] = "filter_class",
        ["filter_activator_model"] = "filter_model",
        ["filter_damage_type"] = "filter_type",
        ["filter_activator_team"] = "filter_team",
        ["filter_activator_mass_greater"] = "filter_class",
        ["filter_activator_attribute_int"] = "filter_class",
        ["filter_enemy"] = "filter_class",
        ["filter_proximity"] = "filter_class",
        ["filter_los"] = "filter_class",
        ["filter_modifier"] = "filter_class",
        ["logic_activityevent"] = "logic_multicompare",
        ["logic_gamestate_report"] = "logic_case",
        ["logic_npc_counter_radius"] = "math_counter",
        ["logic_npc_counter_aabb"] = "math_counter",
        ["logic_npc_counter_obb"] = "math_counter",
    };

    // Owns the decoded images; classnames that alias onto the same material share one entry.
    private readonly Dictionary<string, SKImage> iconsByMaterial = [];
    private readonly Dictionary<string, SKImage> iconsByClassname = [];

    private void LoadEntityIcons()
    {
        var failedMaterials = new HashSet<string>();

        foreach (var island in islands)
        {
            foreach (var node in island)
            {
                if (node.Tag is not EntityLump.Entity entity)
                {
                    continue;
                }

                var classname = entity.GetStringProperty("classname");

                if (string.IsNullOrEmpty(classname))
                {
                    continue;
                }

                if (!iconsByClassname.ContainsKey(classname))
                {
                    var materialName = IconAliases.GetValueOrDefault(classname, classname);

                    if (failedMaterials.Contains(materialName))
                    {
                        continue;
                    }

                    if (!iconsByMaterial.TryGetValue(materialName, out var image))
                    {
                        image = TryLoadIcon(materialName);

                        if (image == null)
                        {
                            failedMaterials.Add(materialName);
                            continue;
                        }

                        iconsByMaterial[materialName] = image;
                    }

                    iconsByClassname[classname] = image;
                }

                node.IconKey = classname;
            }
        }
    }

    private SKImage? TryLoadIcon(string iconName)
    {
        try
        {
            if (RendererContext.FileLoader.LoadFileCompiled($"materials/editor/{iconName}.vmat")?.DataBlock is not Material material)
            {
                return null;
            }

            if (!material.TextureParams.TryGetValue("g_tColor", out var texturePath))
            {
                texturePath = material.TextureParams.Values.FirstOrDefault();
            }

            if (texturePath == null || RendererContext.FileLoader.LoadFileCompiled(texturePath)?.DataBlock is not Texture texture)
            {
                return null;
            }

            using var bitmap = texture.GenerateBitmap();
            return SKImage.FromBitmap(bitmap);
        }
        catch (Exception e)
        {
            Log.Debug(nameof(EntityIOGraphViewer), $"Failed to load editor icon {iconName}: {e.Message}");
            return null;
        }
    }

    /// <summary>Whether <paramref name="entity"/> has a node <see cref="ShowEntity"/> can jump to.</summary>
    public bool HasEntity(EntityLump.Entity entity) => FindNode(entity) != null;

    private GraphNode? FindNode(EntityLump.Entity entity)
    {
        foreach (var island in islands)
        {
            foreach (var node in island)
            {
                if (ReferenceEquals(node.Tag, entity))
                {
                    return node;
                }
            }
        }

        // Merged name-group nodes carry only one member as Tag; match the rest by name+class.
        var name = entity.GetStringProperty("targetname");

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var classname = entity.GetStringProperty("classname");

        foreach (var island in islands)
        {
            foreach (var node in island)
            {
                if (node.Tag is EntityLump.Entity member &&
                    string.Equals(member.GetStringProperty("targetname"), name, StringComparison.OrdinalIgnoreCase) &&
                    member.GetStringProperty("classname") == classname)
                {
                    return node;
                }
            }
        }

        return null;
    }

    /// <summary>Selects and centers the node of <paramref name="entity"/>. Returns false when the entity has no node in the graph.</summary>
    public bool ShowEntity(EntityLump.Entity entity)
    {
        var target = FindNode(entity);

        if (target == null)
        {
            return false;
        }

        if (target.Hidden)
        {
            FocusIslandOf(target);
        }

        if (UiControl?.Parent is TabPage tabPage && tabPage.Parent is TabControl tabControl)
        {
            tabControl.SelectTab(tabPage);
        }

        FocusNode(target);
        return true;
    }

    /// <summary>Double clicking an entity shows it in the map viewer; the asset a node references
    /// stays available through the context menu.</summary>
    protected override void OnNodeDoubleClick(GraphNode node)
    {
        if (showInMap != null && node.Tag is EntityLump.Entity entity)
        {
            showInMap(nodeMembers.GetValueOrDefault(node) ?? [entity]);
            return;
        }

        base.OnNodeDoubleClick(node);
    }

    protected override void AddNodeContextMenuItems(ThemedContextMenuStrip menu, GraphNode node)
    {
        if (showInMap != null && node.Tag is EntityLump.Entity entity)
        {
            var item = new ToolStripMenuItem("Show in map viewer");
            item.Click += (_, _) => showInMap(nodeMembers.GetValueOrDefault(node) ?? [entity]);
            menu.Items.Add(item);
        }
    }

    protected override string BuildStatsText(int islandCount) => $"{entityCount} entities\n{base.BuildStatsText(islandCount)}\nIsland: {focusedIslandName ?? "(all)"}";

    public override void Dispose()
    {
        base.Dispose();

        foreach (var image in iconsByMaterial.Values)
        {
            image.Dispose();
        }

        iconsByMaterial.Clear();
        iconsByClassname.Clear();
    }

    protected override bool HasMultipleIslands => islands.Count > 1;

    protected override void FocusIslandOf(GraphNode node)
    {
        base.FocusIslandOf(node);

        var index = islands.FindIndex(island => island.Contains(node));
        SetIslandLabel(index >= 0 ? IslandLabel(islands[index]) : null);
    }

    protected override void ShowAllIslands()
    {
        base.ShowAllIslands();
        SetIslandLabel(null);
    }

    private void SetIslandLabel(string? islandName)
    {
        focusedIslandName = islandName;
        RefreshStatsLabel();
    }

    private static string IslandLabel(List<GraphNode> island)
    {
        GraphNode? best = null;
        var bestDegree = -1;

        foreach (var node in island)
        {
            var degree = node.Inputs.Count + node.Outputs.Count;

            if (degree > bestDegree)
            {
                bestDegree = degree;
                best = node;
            }
        }

        return $"{best!.Title} ({island.Count} nodes)";
    }

}
