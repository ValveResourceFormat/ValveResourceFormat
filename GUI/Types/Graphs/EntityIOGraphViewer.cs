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

namespace GUI.Types.Graphs;

/// <summary>
/// Graph viewer for the entity I/O system of an entity lump: entities as nodes,
/// output-to-input connections as labeled wires.
/// </summary>
internal class EntityIOGraphViewer : GLGraphViewer
{
    private const GraphHue OutputHue = GraphHue.Orange;
    private const GraphHue InputHue = GraphHue.Cyan;

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
        BuildGraph(View, entities, nodeMembers);

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

    // Prefab-instanced entities carry a "[PR#]" targetname prefix, hide it for display.
    private static string StripTargetnamePrefix(string value)
    {
        const string Prefix = "[PR#]";
        return value.StartsWith(Prefix, StringComparison.Ordinal) ? value[Prefix.Length..] : value;
    }

    private static GraphHue ClassHue(string classname) => EntityClassHues.For(classname);

    private static string? FormatConnectionLabel(Connection connection)
    {
        var parts = new List<string>(3);

        if (connection.Delay > 0f)
        {
            parts.Add($"{connection.Delay:0.##}s");
        }

        if (connection.TimesToFire == 1)
        {
            parts.Add("once");
        }
        else if (connection.TimesToFire > 1)
        {
            parts.Add($"×{connection.TimesToFire}");
        }

        if (!string.IsNullOrEmpty(connection.OverrideParam) && connection.OverrideParam != "(null)")
        {
            parts.Add($"({connection.OverrideParam})");
        }

        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static string? SpecialTargetName(Connection connection) => connection.TargetName.Length > 0
        ? connection.TargetName
        : connection.TargetType switch
        {
            EntityIOTargetType.SpecialActivator => "!activator",
            EntityIOTargetType.SpecialCaller => "!caller",
            _ => null,
        };

    private const int MaxMalformedConnectionWarnings = 20;

    /// <summary>
    /// Fills <paramref name="view"/> with a node per entity and a wire per connection. Leaves the
    /// nodes unpositioned; the caller lays out once the node content is final.
    /// </summary>
    internal static void BuildGraph(GraphView view, List<EntityLump.Entity> entities, Dictionary<GraphNode, List<EntityLump.Entity>>? groupMembers = null)
    {
        var connections = new List<Connection>();
        var malformedConnections = 0;

        foreach (var entity in entities)
        {
            if (entity.Connections == null)
            {
                continue;
            }

            foreach (var connection in entity.Connections)
            {
                if (connection.OutputName == null || connection.InputName == null || connection.TargetName == null)
                {
                    malformedConnections++;

                    if (malformedConnections <= MaxMalformedConnectionWarnings)
                    {
                        var owner = entity.TargetName ?? entity.GetStringProperty("classname") ?? "unknown entity";
                        Log.Warn(nameof(EntityIOGraphViewer), $"Skipping connection with a missing or non-string name field on '{owner}'.");
                    }

                    continue;
                }

                connections.Add(connection);
            }
        }

        if (malformedConnections > MaxMalformedConnectionWarnings)
        {
            Log.Warn(nameof(EntityIOGraphViewer), $"Skipped {malformedConnections} connections with a missing or non-string name field.");
        }

        var resolver = new EntityIOTargetResolver(entities);

        var entityNodes = new Dictionary<EntityLump.Entity, GraphNode>();
        var namedNodes = new Dictionary<(string Name, string Class), GraphNode>();
        var syntheticNodes = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var annotations = new List<(GraphNode Node, string Text)>();
        var outputSockets = new Dictionary<(GraphNode Node, string Name), GraphSocket>();
        var inputSockets = new Dictionary<(GraphNode Node, string Name), GraphSocket>();
        var mergedWires = new Dictionary<(GraphSocket From, GraphSocket To), GraphWire>();

        GraphNode NodeFor(EntityLump.Entity entity)
        {
            if (entityNodes.TryGetValue(entity, out var node))
            {
                return node;
            }

            var classname = entity.GetStringProperty("classname") ?? "unknown";
            var name = entity.GetStringProperty("targetname");

            if (!string.IsNullOrEmpty(name))
            {
                // Entities sharing a targetname form one addressable group (inputs fire on all
                // members at once), so same-name same-class entities merge into one node.
                var key = (name.ToLowerInvariant(), classname);

                if (!namedNodes.TryGetValue(key, out node))
                {
                    var members = resolver.GetByTargetName(name)
                        .Where(e => (e.GetStringProperty("classname") ?? "unknown") == classname)
                        .ToList();

                    if (members.Count == 0)
                    {
                        members = [entity];
                    }

                    node = view.AddNode(new GraphNode
                    {
                        Title = members.Count > 1 ? $"{StripTargetnamePrefix(name)}  ×{members.Count}" : StripTargetnamePrefix(name),
                        Subtitle = classname,
                        Category = ClassHue(classname),
                        Tag = entity,
                    });
                    namedNodes[key] = node;

                    if (groupMembers != null && members.Count > 1)
                    {
                        groupMembers[node] = members;
                    }
                }
            }
            else
            {
                node = view.AddNode(new GraphNode
                {
                    Title = classname,
                    Subtitle = classname,
                    Category = ClassHue(classname),
                    Tag = entity,
                });
            }

            // Double click and the context menu jump to the asset the entity references.
            node.ExternalResourceName ??= entity.GetStringProperty("model") ?? entity.GetStringProperty("effect_name");

            entityNodes[entity] = node;
            return node;
        }

        GraphNode SyntheticFor(string targetName, EntityIOTargetOutcome outcome)
        {
            if (!syntheticNodes.TryGetValue(targetName, out var node))
            {
                var unresolved = outcome == EntityIOTargetOutcome.NotFound;

                node = view.AddNode(new GraphNode
                {
                    Title = StripTargetnamePrefix(targetName),
                    Subtitle = unresolved ? "unresolved target" : "special target",
                    Category = unresolved ? GraphHue.Red : GraphHue.Magenta,
                });
                syntheticNodes[targetName] = node;
            }

            return node;
        }

        GraphSocket OutputFor(GraphNode node, string outputName, GraphHue hue)
        {
            if (!outputSockets.TryGetValue((node, outputName), out var socket))
            {
                socket = node.AddOutput(outputName, hue);
                outputSockets[(node, outputName)] = socket;
            }

            return socket;
        }

        GraphSocket InputFor(GraphNode node, string inputName, GraphHue hue)
        {
            if (!inputSockets.TryGetValue((node, inputName), out var socket))
            {
                socket = node.AddInput(inputName, hue, allowMultiple: true);
                inputSockets[(node, inputName)] = socket;
            }

            return socket;
        }

        // point_template spawn lists: dashed wires to every templateNN child entity.
        foreach (var entity in entities)
        {
            if (entity.GetStringProperty("classname") != "point_template")
            {
                continue;
            }

            for (var i = 1; i <= 64; i++)
            {
                var childName = entity.GetStringProperty($"template{i:D2}");
                var childEntities = resolver.GetByTargetName(childName);

                if (childEntities.Count == 0)
                {
                    continue;
                }

                var output = OutputFor(NodeFor(entity), "spawns", GraphHue.Purple);

                foreach (var childEntity in childEntities)
                {
                    var input = InputFor(NodeFor(childEntity), "spawned by", GraphHue.Purple);

                    if (!mergedWires.ContainsKey((output, input)))
                    {
                        mergedWires[(output, input)] = view.Connect(output, input, dashed: true);
                    }
                }
            }
        }

        if (connections.Count == 0 && mergedWires.Count == 0)
        {
            var infoNode = view.AddNode(new GraphNode { Title = "No entity I/O", Subtitle = "EntityLump" });
            infoNode.AddText($"{entities.Count} entities, no connections");
            return;
        }

        var targetEntities = new List<EntityLump.Entity>();

        foreach (var connection in connections)
        {
            var sourceNode = NodeFor(connection.SourceEntity);

            targetEntities.Clear();
            var outcome = resolver.Resolve(connection.TargetName, connection.TargetType, targetEntities);

            // Targets bound at runtime inline as annotation rows on the firing node instead of wires.
            if (outcome is EntityIOTargetOutcome.Special or EntityIOTargetOutcome.Empty)
            {
                var inlineLabel = FormatConnectionLabel(connection);
                var specialName = SpecialTargetName(connection);
                var inputPart = connection.InputName.Length == 0 ? string.Empty : $".{connection.InputName}";
                var text = specialName == null
                    ? $"{connection.OutputName} → (hook)"
                    : $"{connection.OutputName} → {specialName}{inputPart}{(inlineLabel != null ? $" ({inlineLabel})" : string.Empty)}";
                annotations.Add((sourceNode, text));
                continue;
            }

            var output = OutputFor(sourceNode, connection.OutputName, OutputHue);

            // Name-group members merge into shared nodes; Distinct avoids doubling labels.
            var targetNodes = outcome == EntityIOTargetOutcome.Matched
                ? targetEntities.Select(NodeFor).Distinct().ToList()
                : [SyntheticFor(connection.TargetName, outcome)];

            var label = FormatConnectionLabel(connection);

            foreach (var targetNode in targetNodes)
            {
                var input = InputFor(targetNode, connection.InputName, InputHue);

                if (mergedWires.TryGetValue((output, input), out var existing))
                {
                    // Same output firing the same input multiple times (e.g. different delays)
                    if (label != null)
                    {
                        existing.Label = existing.Label == null ? label : $"{existing.Label} | {label}";
                    }

                    continue;
                }

                mergedWires[(output, input)] = view.Connect(output, input, label: label);
            }
        }

        foreach (var node in entityNodes.Values.Concat(syntheticNodes.Values))
        {
            node.PairSocketRows();
        }

        // After pairing, so the annotation rows sit below the socket lines.
        foreach (var (node, text) in annotations)
        {
            node.AddAnnotation(text, GraphHue.Magenta);
        }

        Log.Debug(nameof(EntityIOGraphViewer), $"Created {entityNodes.Count + syntheticNodes.Count} nodes from {connections.Count} connections ({entities.Count} entities).");
    }
}
