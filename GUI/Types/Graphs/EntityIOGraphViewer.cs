using System.Linq;
using GUI.Types.GLViewers;
using GUI.Types.Graphs.Core;
using GUI.Utils;
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

    public EntityIOGraphViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, EntityLump entityLump)
        : base(vrfGuiContext, rendererContext, new GraphView())
    {
        BuildGraph(View, entityLump);

        islands = View.GetComponents();
        islands.Sort(static (a, b) => b.Count.CompareTo(a.Count));
    }

    protected override bool ShowSidebar => islands.Count > 1;

    protected override void AddUiControls()
    {
        base.AddUiControls();

        if (islands.Count < 2 || UiControl == null)
        {
            return;
        }

        var combo = UiControl.AddSelection("Island", (_, index) => ShowIsland(index - 1));

        combo.BeginUpdate();

        var totalNodes = 0;

        foreach (var island in islands)
        {
            totalNodes += island.Count;
        }

        combo.Items.Add($"All islands ({totalNodes} nodes)");

        foreach (var island in islands)
        {
            combo.Items.Add(IslandLabel(island));
        }

        combo.EndUpdate();
        combo.SelectedIndex = 0;
    }

    private void ShowIsland(int islandIndex)
    {
        for (var i = 0; i < islands.Count; i++)
        {
            var visible = islandIndex < 0 || i == islandIndex;

            foreach (var node in islands[i])
            {
                node.Hidden = !visible;
            }
        }

        RefitToGraph();
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

    private static GraphHue ClassHue(string classname)
    {
        if (classname.StartsWith("trigger_", StringComparison.Ordinal))
        {
            return GraphHue.Green;
        }

        if (classname.StartsWith("logic_", StringComparison.Ordinal) ||
            classname.StartsWith("math_", StringComparison.Ordinal) ||
            classname.StartsWith("filter_", StringComparison.Ordinal))
        {
            return GraphHue.Amber;
        }

        if (classname.StartsWith("func_", StringComparison.Ordinal))
        {
            return GraphHue.Blue;
        }

        if (classname.StartsWith("point_", StringComparison.Ordinal))
        {
            return GraphHue.Purple;
        }

        if (classname.StartsWith("env_", StringComparison.Ordinal))
        {
            return GraphHue.Teal;
        }

        if (classname.StartsWith("prop_", StringComparison.Ordinal))
        {
            return GraphHue.Slate;
        }

        if (classname.StartsWith("game_", StringComparison.Ordinal) ||
            classname.StartsWith("player_", StringComparison.Ordinal))
        {
            return GraphHue.Maroon;
        }

        if (classname.StartsWith("snd_", StringComparison.Ordinal) ||
            classname.StartsWith("ambient_", StringComparison.Ordinal))
        {
            return GraphHue.Cyan;
        }

        return GraphHue.Neutral;
    }

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

    internal static void BuildGraph(GraphView view, EntityLump entityLump)
    {
        var entities = entityLump.GetEntities();
        var connections = new List<Connection>();

        foreach (var entity in entities)
        {
            if (entity.Connections == null)
            {
                continue;
            }

            foreach (var connectionData in entity.Connections)
            {
                connections.Add(new Connection(
                    entity,
                    connectionData.GetStringProperty("m_outputName"),
                    connectionData.GetStringProperty("m_inputName"),
                    connectionData.GetStringProperty("m_targetName"),
                    connectionData.GetStringProperty("m_overrideParam"),
                    connectionData.GetFloatProperty("m_flDelay"),
                    connectionData.GetInt32Property("m_nTimesToFire")));
            }
        }

        if (connections.Count == 0)
        {
            var infoNode = view.AddNode(new GraphNode { Title = "No entity I/O", Subtitle = "EntityLump" });
            infoNode.AddText($"{entities.Count} entities, no connections");
            return;
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
        var syntheticNodes = new Dictionary<string, GraphNode>(StringComparer.OrdinalIgnoreCase);
        var outputSockets = new Dictionary<(GraphNode Node, string Name), GraphSocket>();
        var inputSockets = new Dictionary<(GraphNode Node, string Name), GraphSocket>();
        var mergedWires = new Dictionary<(GraphSocket From, GraphSocket To), GraphWire>();

        GraphNode NodeFor(EntityLump.Entity entity)
        {
            if (!entityNodes.TryGetValue(entity, out var node))
            {
                var classname = entity.GetStringProperty("classname") ?? "unknown";
                var name = entity.GetStringProperty("targetname");

                node = view.AddNode(new GraphNode
                {
                    Title = string.IsNullOrEmpty(name) ? classname : StripTargetnamePrefix(name),
                    Subtitle = classname,
                    Category = ClassHue(classname),
                });
                entityNodes[entity] = node;
            }

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

        GraphSocket OutputFor(GraphNode node, string outputName)
        {
            if (!outputSockets.TryGetValue((node, outputName), out var socket))
            {
                socket = node.AddOutput(outputName, OutputHue);
                outputSockets[(node, outputName)] = socket;
            }

            return socket;
        }

        GraphSocket InputFor(GraphNode node, string inputName)
        {
            if (!inputSockets.TryGetValue((node, inputName), out var socket))
            {
                socket = node.AddInput(inputName, InputHue, allowMultiple: true);
                inputSockets[(node, inputName)] = socket;
            }

            return socket;
        }

        foreach (var connection in connections)
        {
            var sourceNode = NodeFor(connection.Source);
            var output = OutputFor(sourceNode, connection.OutputName);

            List<GraphNode> targetNodes;

            if (connection.TargetName.StartsWith('!'))
            {
                targetNodes = [SyntheticFor(connection.TargetName, unresolved: false)];
            }
            else if (entitiesByName.TryGetValue(connection.TargetName, out var targetEntities))
            {
                targetNodes = targetEntities.Select(NodeFor).ToList();
            }
            else
            {
                targetNodes = [SyntheticFor(connection.TargetName, unresolved: true)];
            }

            var label = FormatConnectionLabel(connection);

            foreach (var targetNode in targetNodes)
            {
                var input = InputFor(targetNode, connection.InputName);

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

        Log.Debug(nameof(EntityIOGraphViewer), $"Created {entityNodes.Count + syntheticNodes.Count} nodes from {connections.Count} connections ({entities.Count} entities).");

        view.LayoutNodesPacked();
    }
}
