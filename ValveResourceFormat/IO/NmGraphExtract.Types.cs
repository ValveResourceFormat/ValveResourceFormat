using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

public sealed partial class NmGraphExtract
{
    private readonly record struct PinDef(string Name, string Type, bool IsDynamicPin = false, bool AllowMultipleOutConnections = false);

    private readonly record struct EventConditionRulesData(string Operator, string SearchRule, string PriorityRule, bool LimitSearchToSourceState, bool IgnoreInactiveBranchEvents);

    private readonly record struct CompiledNodeClass(string Name)
    {
        private const string Prefix = "CNm";
        private const string Suffix = "Node::CDefinition";

        public string Stem
        {
            get
            {
                if (!Name.StartsWith(Prefix, StringComparison.Ordinal) || !Name.EndsWith(Suffix, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Unsupported compiled NmGraph class name: {Name}");
                }

                return Name[Prefix.Length..^Suffix.Length];
            }
        }

        public bool TryGetTypedSuffix(string prefix, out string valueType)
        {
            if (Stem.StartsWith(prefix, StringComparison.Ordinal))
            {
                valueType = Stem[prefix.Length..];
                return !string.IsNullOrEmpty(valueType);
            }

            valueType = string.Empty;
            return false;
        }
    }

    private sealed class FlowGraphBuilder
    {
        public string GraphKey { get; }
        public string GraphType { get; }
        public Dictionary<int, string> NodeIdsByCompiledIndex { get; } = [];
        public List<KVObject> Nodes { get; } = [];
        public List<KVObject> Connections { get; } = [];

        public FlowGraphBuilder(string graphKey, string graphType)
        {
            GraphKey = graphKey;
            GraphType = graphType;
        }

        public void Connect(string fromNodeId, string outputPinId, string toNodeId, string inputPinId)
        {
            var connection = KVObject.Collection();
            connection.Add("m_ID", MakeGuid($"connection:{GraphKey}:{fromNodeId}:{outputPinId}:{toNodeId}:{inputPinId}"));
            connection.Add("m_fromNodeID", fromNodeId);
            connection.Add("m_outputPinID", outputPinId);
            connection.Add("m_toNodeID", toNodeId);
            connection.Add("m_inputPinID", inputPinId);
            Connections.Add(connection);
        }

        public KVObject ToGraph()
        {
            ApplyDefaultNodeLayout();

            var graph = KVObject.Collection();
            graph.Add("_class", "CNmGraphDocFlowGraph");
            graph.Add("m_ID", MakeGuid($"graph:{GraphKey}"));

            var nodesArray = KVObject.Array();
            foreach (var node in Nodes)
            {
                nodesArray.Add(node);
            }

            var connectionsArray = KVObject.Array();
            foreach (var connection in Connections)
            {
                connectionsArray.Add(connection);
            }

            graph.Add("m_nodes", nodesArray);
            graph.Add("m_graphType", GraphType);
            graph.Add("m_viewOffset", MakeVector2(0.0f, 0.0f));
            graph.Add("m_flViewZoom", 1.0f);
            graph.Add("m_connections", connectionsArray);
            return graph;
        }

        private void ApplyDefaultNodeLayout()
        {
            var autoLayoutNodes = Nodes
                .Where(NeedsAutoLayout)
                .ToArray();

            if (autoLayoutNodes.Length == 0)
            {
                return;
            }

            var autoLayoutNodeIds = autoLayoutNodes
                .Select(node => node.GetStringProperty("m_ID"))
                .ToHashSet(StringComparer.Ordinal);
            var incomingNodeIds = autoLayoutNodeIds.ToDictionary(nodeId => nodeId, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
            var positionedNodes = Nodes
                .Where(node => !NeedsAutoLayout(node) && !ShouldIgnoreForAutoLayoutAnchoring(node))
                .Select(node => (Node: node, Position: node.GetArray<float>("m_position")))
                .Where(entry => entry.Position is { Length: 2 })
                .ToArray();
            var anchoredLayerByNodeId = positionedNodes.ToDictionary(
                entry => entry.Node.GetStringProperty("m_ID"),
                entry => (int)MathF.Round(entry.Position![0] / NodeColumnSpacing),
                StringComparer.Ordinal);
            var rowByLayer = new Dictionary<int, int>();

            foreach (var (_, position) in positionedNodes)
            {
                var layer = (int)MathF.Round(position![0] / NodeColumnSpacing);
                var row = (int)MathF.Round(position[1] / NodeRowSpacing);
                rowByLayer[layer] = Math.Max(rowByLayer.GetValueOrDefault(layer), row + 1);
            }

            foreach (var connection in Connections)
            {
                var fromNodeId = connection.GetStringProperty("m_fromNodeID");
                var toNodeId = connection.GetStringProperty("m_toNodeID");

                if (!autoLayoutNodeIds.Contains(fromNodeId) || !autoLayoutNodeIds.Contains(toNodeId))
                {
                    continue;
                }

                incomingNodeIds[toNodeId].Add(fromNodeId);
            }

            var orderedNodeIds = autoLayoutNodes
                .Select(node => node.GetStringProperty("m_ID"))
                .ToArray();
            var nodesById = autoLayoutNodes.ToDictionary(node => node.GetStringProperty("m_ID"), StringComparer.Ordinal);
            var queue = new Queue<string>(orderedNodeIds.Where(nodeId => incomingNodeIds[nodeId].Count == 0));
            var topologicalOrder = new List<string>(orderedNodeIds.Length);

            while (queue.Count > 0)
            {
                var nodeId = queue.Dequeue();
                topologicalOrder.Add(nodeId);

                foreach (var connection in Connections)
                {
                    if (!nodeId.Equals(connection.GetStringProperty("m_fromNodeID"), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var toNodeId = connection.GetStringProperty("m_toNodeID");
                    if (!autoLayoutNodeIds.Contains(toNodeId))
                    {
                        continue;
                    }

                    if (incomingNodeIds[toNodeId].Remove(nodeId) && incomingNodeIds[toNodeId].Count == 0)
                    {
                        queue.Enqueue(toNodeId);
                    }
                }
            }

            if (topologicalOrder.Count != orderedNodeIds.Length)
            {
                topologicalOrder = [.. orderedNodeIds];
            }

            var layerByNodeId = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var nodeId in topologicalOrder)
            {
                var layer = 0;

                foreach (var connection in Connections)
                {
                    if (!nodeId.Equals(connection.GetStringProperty("m_toNodeID"), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var fromNodeId = connection.GetStringProperty("m_fromNodeID");
                    if (layerByNodeId.TryGetValue(fromNodeId, out var fromLayer))
                    {
                        layer = Math.Max(layer, fromLayer + 1);
                    }
                    else if (anchoredLayerByNodeId.TryGetValue(fromNodeId, out var anchoredFromLayer))
                    {
                        layer = Math.Max(layer, anchoredFromLayer + 1);
                    }
                }

                foreach (var connection in Connections)
                {
                    if (!nodeId.Equals(connection.GetStringProperty("m_fromNodeID"), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var toNodeId = connection.GetStringProperty("m_toNodeID");
                    if (!anchoredLayerByNodeId.TryGetValue(toNodeId, out var anchoredToLayer))
                    {
                        continue;
                    }

                    layer = Math.Max(layer, anchoredToLayer - 1);
                }

                layerByNodeId[nodeId] = layer;
            }

            foreach (var nodeId in topologicalOrder)
            {
                var layer = layerByNodeId.GetValueOrDefault(nodeId);
                var row = rowByLayer.GetValueOrDefault(layer);
                rowByLayer[layer] = row + 1;

                var node = nodesById[nodeId];
                node["m_position"] = MakeVector2(layer * NodeColumnSpacing, row * NodeRowSpacing);
            }
        }

        private static bool NeedsAutoLayout(KVObject node)
        {
            if (!node.TryGetValue("m_position", out var value) || !value.IsArray)
            {
                return true;
            }

            var span = value.AsArraySpan();
            if (span.Length != 2)
            {
                return true;
            }

            var x = (float)span[0];
            var y = (float)span[1];
            return x == 0.0f && y == 0.0f;
        }

        private static bool ShouldIgnoreForAutoLayoutAnchoring(KVObject node)
        {
            var className = node.GetStringProperty("_class");
            return className.Contains("ControlParameterNode", StringComparison.Ordinal)
                || className.Contains("VirtualParameterNode", StringComparison.Ordinal);
        }
    }

    private enum StateMachineTransitionGroup
    {
        Standard,
        Global,
    }

    private sealed class TransitionInfo
    {
        public StateMachineTransitionGroup GroupKind { get; init; }
        public string GroupPath { get; init; } = string.Empty;
        public int SourceStateNodeIndex { get; init; }
        public int TargetStateIndex { get; init; }
        public int TargetStateNodeIndex { get; init; }
        public int ConditionNodeIndex { get; init; }
        public int TransitionNodeIndex { get; init; }
        public KVObject CompiledTransitionNode { get; init; } = null!;
        public KVObject StateMachineTransition { get; init; } = null!;
        public bool CanBeForced { get; init; }
    }
}
