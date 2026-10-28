using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

public partial class AnimationGraphExtract
{
    /// <summary>
    /// A group the compiler inlined, identified by the node path prefix its nodes share.
    /// </summary>
    private sealed class NodeGroup(long nodeId, long inputNodeId, long outputNodeId)
    {
        public long NodeId { get; } = nodeId;
        public long InputNodeId { get; } = inputNodeId;
        public long OutputNodeId { get; } = outputNodeId;
        public List<KVObject> Nodes { get; } = [];
        public List<NodeGroup> Groups { get; } = [];
        public List<KVObject> InputProxies { get; } = [];
        public List<KVObject> OutputProxies { get; } = [];
        public List<KVObject> InputConnectionMap { get; } = [];
        public Dictionary<(long Node, long Output), long> InputsBySource { get; } = [];
        public Dictionary<(long Node, long Output), long> OutputsBySource { get; } = [];
    }

    /// <summary>
    /// The ids of the groups enclosing a compiled node, outermost first. The last id of the path is
    /// the node itself.
    /// </summary>
    private static long[] GetGroupPath(KVObject compiledNode)
    {
        var nodePath = compiledNode.GetSubCollection("m_nodePath");
        if (nodePath?.GetArray("m_path") is not { } path)
        {
            return [];
        }

        var count = Math.Min((int)nodePath.GetIntegerProperty("m_nCount"), path.Count);

        return path
            .Take(count - 1)
            .Select(id => id.GetIntegerProperty("m_id"))
            .Where(id => id != uint.MaxValue)
            .ToArray();
    }

    /// <summary>
    /// Adds the nodes to the node list inside the groups they were authored in. Groups and subgraphs
    /// are inlined by the compiler, which only keeps the path of enclosing ids on each node and wires
    /// nodes to each other directly, so a connection that crosses the edge of a group is routed back
    /// through the proxies of its input and output nodes.
    /// </summary>
    private void AddNodesInGroups(KVObject nodeList, Dictionary<long, KVObject> nodes, Dictionary<long, long[]> groupPaths)
    {
        var assignedIds = new HashSet<long>(nodes.Keys);
        var topLevel = new NodeGroup(InvalidEditorNodeId, InvalidEditorNodeId, InvalidEditorNodeId);
        var groups = new Dictionary<string, NodeGroup>(StringComparer.Ordinal) { [string.Empty] = topLevel };

        foreach (var (nodeId, node) in nodes)
        {
            GetGroup(groupPaths[nodeId]).Nodes.Add(MakeNodeManagerEntry(nodeId, node));
        }

        foreach (var (nodeId, node) in nodes)
        {
            var consumerPath = groupPaths[nodeId];

            foreach (var connection in FindConnections(node))
            {
                var source = connection.GetSubCollection("m_nodeID").GetIntegerProperty("m_id");

                // Connections to group proxies are already in place
                if (connection.GetSubCollection("m_outputID").GetIntegerProperty("m_id") != source
                    || !groupPaths.TryGetValue(source, out var sourcePath))
                {
                    continue;
                }

                var common = consumerPath.Zip(sourcePath).TakeWhile(pair => pair.First == pair.Second).Count();
                if (common == consumerPath.Length && common == sourcePath.Length)
                {
                    continue;
                }

                var name = nodes[source].GetStringProperty("m_sName") ?? "Unnamed";
                var (sourceNode, sourceOutput) = (source, source);

                for (var depth = sourcePath.Length; depth > common; depth--)
                {
                    var group = GetGroup(sourcePath[..depth]);
                    sourceOutput = GetOutputProxy(group, sourceNode, sourceOutput, name);
                    sourceNode = group.NodeId;
                }

                for (var depth = common + 1; depth <= consumerPath.Length; depth++)
                {
                    var group = GetGroup(consumerPath[..depth]);
                    sourceOutput = GetInputProxy(group, sourceNode, sourceOutput, name);
                    sourceNode = group.InputNodeId;
                }

                connection["m_nodeID"] = MakeNodeIdObjectValue(sourceNode);
                connection["m_outputID"] = MakeNodeIdObjectValue(sourceOutput);
            }
        }

        foreach (var entry in topLevel.Nodes.Concat(topLevel.Groups.Select(MakeGroupEntry)))
        {
            nodeList.Add(entry);
        }

        NodeGroup GetGroup(long[] path)
        {
            var key = string.Join(',', path.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            if (groups.TryGetValue(key, out var group))
            {
                return group;
            }

            var parent = GetGroup(path[..^1]);

            // Every instance of a subgraph shares the ids of its groups
            var groupId = path[^1];
            if (issuedNodeIds.Contains(groupId) || !assignedIds.Add(groupId))
            {
                groupId = GenerateNewNodeId(assignedIds, ref generatedNodeIdCursor);
            }

            group = new NodeGroup(
                groupId,
                GenerateNewNodeId(assignedIds, ref generatedNodeIdCursor),
                GenerateNewNodeId(assignedIds, ref generatedNodeIdCursor));

            parent.Groups.Add(group);
            groups[key] = group;

            return group;
        }

        long GetOutputProxy(NodeGroup group, long sourceNode, long sourceOutput, string name)
        {
            if (!group.OutputsBySource.TryGetValue((sourceNode, sourceOutput), out var proxyId))
            {
                proxyId = GenerateNewNodeId(assignedIds, ref generatedNodeIdCursor);
                group.OutputsBySource[(sourceNode, sourceOutput)] = proxyId;
                group.OutputProxies.Add(MakeProxyItem(name, proxyId, MakeInputConnection(sourceNode, sourceOutput)));
            }

            return proxyId;
        }

        long GetInputProxy(NodeGroup group, long sourceNode, long sourceOutput, string name)
        {
            if (!group.InputsBySource.TryGetValue((sourceNode, sourceOutput), out var proxyId))
            {
                proxyId = GenerateNewNodeId(assignedIds, ref generatedNodeIdCursor);
                group.InputsBySource[(sourceNode, sourceOutput)] = proxyId;
                group.InputProxies.Add(MakeProxyItem(name, proxyId, MakeInputConnection(InvalidEditorNodeId)));
                group.InputConnectionMap.Add(new KVObject
                {
                    { "key", MakeNodeIdObjectValue(proxyId) },
                    { "value", MakeInputConnection(sourceNode, sourceOutput) },
                });
            }

            return proxyId;
        }
    }

    private static KVObject MakeGroupEntry(NodeGroup group)
    {
        var groupNodes = new List<KVObject>
        {
            MakeNodeManagerEntry(group.InputNodeId, MakeGroupProxyNode("CGroupInputAnimNode", group.InputNodeId, group.InputProxies)),
            MakeNodeManagerEntry(group.OutputNodeId, MakeGroupProxyNode("CGroupOutputAnimNode", group.OutputNodeId, group.OutputProxies)),
        };
        groupNodes.AddRange(group.Nodes);
        groupNodes.AddRange(group.Groups.Select(MakeGroupEntry));

        var node = MakeNode(
            "CGroupAnimNode",
            [
                ("m_sName", "Group"),
                ("m_nNodeID", MakeNodeIdObjectValue(group.NodeId)),
                ("m_networkMode", "ServerAuthoritative"),
                ("m_inputNodeID", MakeNodeIdObjectValue(group.InputNodeId)),
                ("m_outputNodeID", MakeNodeIdObjectValue(group.OutputNodeId)),
                ("m_inputConnectionMap", MakeArray(group.InputConnectionMap)),
                ("m_nodeMgr", MakeNode("CAnimNodeManager", ("m_nodes", MakeArray(groupNodes)))),
            ]);

        return MakeNodeManagerEntry(group.NodeId, node);
    }

    private static KVObject MakeGroupProxyNode(string className, long nodeId, List<KVObject> proxyItems)
        => MakeNode(
            className,
            [
                ("m_sName", "Unnamed"),
                ("m_nNodeID", MakeNodeIdObjectValue(nodeId)),
                ("m_networkMode", "ServerAuthoritative"),
                ("m_proxyItems", MakeArray(proxyItems)),
            ]);

    private static KVObject MakeProxyItem(string name, long outputId, KVObject inputConnection) => new()
    {
        { "m_name", name },
        { "m_outputID", MakeNodeIdObjectValue(outputId) },
        { "m_inputConnection", inputConnection },
    };

    /// <summary>Every node connection held anywhere inside a converted node.</summary>
    private static List<KVObject> FindConnections(KVObject node)
    {
        var connections = new List<KVObject>();

        Visit(node);

        return connections;

        void Visit(KVObject value)
        {
            if (value.ContainsKey("m_nodeID") && value.ContainsKey("m_outputID"))
            {
                connections.Add(value);
                return;
            }

            foreach (var (_, child) in value)
            {
                if (child is KVObject childObject)
                {
                    Visit(childObject);
                }
            }
        }
    }
}
