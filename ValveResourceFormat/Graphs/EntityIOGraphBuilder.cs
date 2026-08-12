using System.Linq;
using ValveResourceFormat.ResourceTypes;
using Connection = ValveResourceFormat.ResourceTypes.EntityLump.Connection;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Graphs;

/// <summary>
/// Builds the entity I/O graph of a map: one node per entity (same-name same-class entities merge
/// into one) and one wire per connection between them.
/// </summary>
public static class EntityIOGraphBuilder
{
    /// <summary>Hue of an entity's outputs and the wires leaving them.</summary>
    public const GraphHue OutputHue = GraphHue.Orange;

    /// <summary>Hue of an entity's inputs and the wires arriving at them.</summary>
    public const GraphHue InputHue = GraphHue.Cyan;

    /// <summary>Receives diagnostics about connections the lump could not be read cleanly from.</summary>
    public static IProgress<string>? ProgressReporter { get; set; }

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
    /// Fills <paramref name="document"/> with a node per entity and a wire per connection. Leaves the
    /// nodes unpositioned; the caller lays out once the node content is final.
    /// </summary>
    /// <param name="document">The graph to fill.</param>
    /// <param name="entities">The entities to build nodes from.</param>
    /// <param name="groupMembers">Receives the entities merged into each name-group node.</param>
    public static void Build(GraphDocument document, List<EntityLump.Entity> entities, Dictionary<GraphNode, List<EntityLump.Entity>>? groupMembers = null)
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
                        ProgressReporter?.Report($"Skipping connection with a missing or non-string name field on '{owner}'.");
                    }

                    continue;
                }

                connections.Add(connection);
            }
        }

        if (malformedConnections > MaxMalformedConnectionWarnings)
        {
            ProgressReporter?.Report($"Skipped {malformedConnections} connections with a missing or non-string name field.");
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

                    node = document.AddNode(new GraphNode
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
                node = document.AddNode(new GraphNode
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

                node = document.AddNode(new GraphNode
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
                        mergedWires[(output, input)] = document.Connect(output, input, dashed: true);
                    }
                }
            }
        }

        if (connections.Count == 0 && mergedWires.Count == 0)
        {
            var infoNode = document.AddNode(new GraphNode { Title = "No entity I/O", Subtitle = "EntityLump" });
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

                mergedWires[(output, input)] = document.Connect(output, input, label: label);
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

        ProgressReporter?.Report($"Created {entityNodes.Count + syntheticNodes.Count} nodes from {connections.Count} connections ({entities.Count} entities).");
    }
}
