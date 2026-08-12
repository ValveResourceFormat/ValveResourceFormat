using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

public sealed partial class NmGraphExtract
{
    private readonly record struct PinDef(string Name, string Type, bool IsDynamicPin = false, bool AllowMultipleOutConnections = false);

    /// <summary>
    /// Event condition rules.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animlib/NmEventConditionRules_t">NmEventConditionRules_t</seealso>
    private enum NmEventConditionRules : byte
    {
        LimitSearchToSourceState = 0,
        IgnoreInactiveEvents = 1,
        PreferHighestWeight = 2,
        PreferHighestProgress = 3,
        OperatorOr = 4,
        OperatorAnd = 5,
        SearchOnlyGraphEvents = 6,
        SearchOnlyAnimEvents = 7,
        SearchBothGraphAndAnimEvents = 8,
    }

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
            connection.Add("m_ID", MakeGuid());
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
            graph.Add("m_ID", MakeGuid());

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
            if (Nodes.Count == 0)
            {
                return;
            }

            var index = new Dictionary<string, int>(Nodes.Count, StringComparer.Ordinal);

            for (var i = 0; i < Nodes.Count; i++)
            {
                index.TryAdd(Nodes[i].GetStringProperty("m_ID"), i);
            }

            var edges = new List<GraphLayoutEdge>(Connections.Count);

            foreach (var connection in Connections)
            {
                if (index.TryGetValue(connection.GetStringProperty("m_fromNodeID"), out var from)
                    && index.TryGetValue(connection.GetStringProperty("m_toNodeID"), out var to)
                    && from != to)
                {
                    edges.Add(GraphPlacement.MakeEdge(from, to));
                }
            }

            var positions = GraphPlacement.Layout(Nodes.Count, [.. edges]);

            for (var i = 0; i < Nodes.Count; i++)
            {
                Nodes[i]["m_position"] = MakeVector2(positions[i].X, positions[i].Y);
            }
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
