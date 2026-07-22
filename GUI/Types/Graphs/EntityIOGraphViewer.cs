using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.GLViewers;
using GUI.Types.Graphs.Core;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

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
        View.IconResolver = key => iconCache.GetValueOrDefault(key);

        // Icons widen their nodes, so lay out again with the final geometry.
        if (iconCache.Count > 0)
        {
            View.LayoutNodesPacked();
        }
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

    private readonly Dictionary<string, SKImage> iconCache = [];

    private void LoadEntityIcons()
    {
        var failed = new HashSet<string>();

        foreach (var island in islands)
        {
            foreach (var node in island)
            {
                if (node.Tag is not EntityLump.Entity entity)
                {
                    continue;
                }

                var classname = entity.GetStringProperty("classname");

                if (string.IsNullOrEmpty(classname) || failed.Contains(classname))
                {
                    continue;
                }

                if (!iconCache.ContainsKey(classname))
                {
                    var image = TryLoadIcon(classname);

                    if (image == null)
                    {
                        failed.Add(classname);
                        continue;
                    }

                    iconCache[classname] = image;
                }

                node.IconKey = classname;
            }
        }
    }

    private SKImage? TryLoadIcon(string classname)
    {
        var iconName = IconAliases.GetValueOrDefault(classname, classname);

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
            Log.Debug(nameof(EntityIOGraphViewer), $"Failed to load editor icon for {classname}: {e.Message}");
            return null;
        }
    }

    /// <summary>Selects and centers the node of <paramref name="entity"/>. Returns false when the entity has no node in the graph.</summary>
    public bool ShowEntity(EntityLump.Entity entity)
    {
        GraphNode? target = null;

        foreach (var island in islands)
        {
            foreach (var node in island)
            {
                if (ReferenceEquals(node.Tag, entity))
                {
                    target = node;
                    break;
                }
            }

            if (target != null)
            {
                break;
            }
        }

        if (target == null)
        {
            // Merged name-group nodes carry only one member as Tag; match the rest by name+class.
            var name = entity.GetStringProperty("targetname");
            var classname = entity.GetStringProperty("classname");

            if (!string.IsNullOrEmpty(name))
            {
                foreach (var island in islands)
                {
                    foreach (var node in island)
                    {
                        if (node.Tag is EntityLump.Entity member &&
                            string.Equals(member.GetStringProperty("targetname"), name, StringComparison.OrdinalIgnoreCase) &&
                            member.GetStringProperty("classname") == classname)
                        {
                            target = node;
                            break;
                        }
                    }

                    if (target != null)
                    {
                        break;
                    }
                }
            }
        }

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

        foreach (var image in iconCache.Values)
        {
            image.Dispose();
        }

        iconCache.Clear();
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

    private sealed record Connection(EntityLump.Entity Source, string OutputName, string InputName, string TargetName, string OverrideParam, float Delay, int TimesToFire);

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

    internal static void BuildGraph(GraphView view, List<EntityLump.Entity> entities, Dictionary<GraphNode, List<EntityLump.Entity>>? groupMembers = null)
    {
        var connections = new List<Connection>();

        foreach (var entity in entities)
        {
            if (entity.Connections == null)
            {
                continue;
            }

            foreach (var connectionData in entity.Connections)
            {
                var outputName = connectionData.GetStringProperty("m_outputName");
                var inputName = connectionData.GetStringProperty("m_inputName");
                var targetName = connectionData.GetStringProperty("m_targetName");

                if (outputName == null || inputName == null || targetName == null)
                {
                    var owner = entity.GetStringProperty("targetname") ?? entity.GetStringProperty("classname") ?? "unknown entity";
                    Log.Warn(nameof(EntityIOGraphViewer), $"Skipping connection with a missing or non-string name field on '{owner}'.");
                    continue;
                }

                connections.Add(new Connection(
                    entity,
                    outputName,
                    inputName,
                    targetName,
                    connectionData.GetStringProperty("m_overrideParam"),
                    connectionData.GetFloatProperty("m_flDelay"),
                    connectionData.GetInt32Property("m_nTimesToFire")));
            }
        }

        var entitiesByName = new Dictionary<string, List<EntityLump.Entity>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            var name = entity.GetStringProperty("targetname");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (!entitiesByName.TryGetValue(name, out var list))
            {
                list = [];
                entitiesByName[name] = list;
            }

            list.Add(entity);
        }

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
                    List<EntityLump.Entity> members = entitiesByName.TryGetValue(name, out var group)
                        ? group.Where(e => (e.GetStringProperty("classname") ?? "unknown") == classname).ToList()
                        : [entity];

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

        GraphNode SyntheticFor(string targetName, bool unresolved)
        {
            if (!syntheticNodes.TryGetValue(targetName, out var node))
            {
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

                if (string.IsNullOrEmpty(childName) || !entitiesByName.TryGetValue(childName, out var childEntities))
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

        foreach (var connection in connections)
        {
            var sourceNode = NodeFor(connection.Source);

            // Relative specials (!self, !activator, ...) and listener hooks are not real map
            // entities; they inline as annotation rows on the firing node instead of wires.
            if (connection.TargetName.Length == 0 || connection.TargetName.StartsWith('!'))
            {
                var inlineLabel = FormatConnectionLabel(connection);
                var inputPart = connection.InputName.Length == 0 ? string.Empty : $".{connection.InputName}";
                var text = connection.TargetName.Length == 0
                    ? $"{connection.OutputName} → (hook)"
                    : $"{connection.OutputName} → {connection.TargetName}{inputPart}{(inlineLabel != null ? $" ({inlineLabel})" : string.Empty)}";
                annotations.Add((sourceNode, text));
                continue;
            }

            var output = OutputFor(sourceNode, connection.OutputName, OutputHue);

            List<GraphNode> targetNodes;

            if (entitiesByName.TryGetValue(connection.TargetName, out var targetEntities))
            {
                // Name-group members merge into shared nodes; Distinct avoids doubling labels.
                targetNodes = targetEntities.Select(NodeFor).Distinct().ToList();
            }
            else if (connection.TargetName.Contains('*'))
            {
                // Source 2 target names support trailing-* prefix matching.
                var prefix = connection.TargetName[..connection.TargetName.IndexOf('*')];
                targetNodes = [];

                foreach (var (name, namedEntities) in entitiesByName)
                {
                    if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var targetEntity in namedEntities)
                        {
                            targetNodes.Add(NodeFor(targetEntity));
                        }
                    }
                }

                // Name-group members merge into shared nodes; Distinct avoids doubling labels.
                targetNodes = targetNodes.Distinct().ToList();

                if (targetNodes.Count == 0)
                {
                    targetNodes = [SyntheticFor(connection.TargetName, unresolved: true)];
                }
            }
            else
            {
                targetNodes = [SyntheticFor(connection.TargetName, unresolved: true)];
            }

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

        // Socket order here is the order the entity lump happened to list the connections in,
        // so the layout is free to reorder the rows to shorten the wires.
        foreach (var node in entityNodes.Values.Concat(syntheticNodes.Values))
        {
            node.PairSocketRows();
            node.SocketOrderFixed = false;

            foreach (var socket in node.Inputs.Concat(node.Outputs))
            {
                socket.OrderFixed = false;
            }
        }

        // After pairing, so the annotation rows sit below the socket lines.
        foreach (var (node, text) in annotations)
        {
            node.AddAnnotation(text, GraphHue.Magenta);
        }

        Log.Debug(nameof(EntityIOGraphViewer), $"Created {entityNodes.Count + syntheticNodes.Count} nodes from {connections.Count} connections ({entities.Count} entities).");

        view.LayoutNodesPacked();
    }
}
