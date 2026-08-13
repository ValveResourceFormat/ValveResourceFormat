using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// The walker for the uncompiled <c>CAnimationGraph</c> layout: nodes are id/value pairs,
    /// children link by <c>m_id</c>, and sequence nodes carry their sequence name as a string.
    /// </summary>
    public static partial class AnimationGraph1Additive
    {
        // A child id of uint.MaxValue is the graph's "no node" sentinel.
        private const long InvalidNodeId = 0xFFFFFFFF;

        private static void CollectFromUncompiledGraph(KVObject root, HashSet<string> result)
        {
            if (!root.ContainsKey("m_nodes"))
            {
                return;
            }

            var nodesById = new Dictionary<long, KVObject>();
            foreach (var entry in root.GetArray("m_nodes"))
            {
                nodesById[entry.GetSubCollection("key").GetIntegerProperty("m_id")] = entry.GetSubCollection("value");
            }

            var additiveRoots = new List<long>();
            foreach (var node in nodesById.Values)
            {
                var nodeClass = node.GetStringProperty("_class");

                if (nodeClass == "CAddAnimNode" && node.ContainsKey("m_additiveChildID"))
                {
                    additiveRoots.Add(node.GetSubCollection("m_additiveChildID").GetIntegerProperty("m_id"));
                }
                else if (nodeClass == "CAimMatrixAnimNode"
                    && node.GetStringProperty("m_blendMode", "").Contains("Additive", StringComparison.Ordinal))
                {
                    AddSequence(node.GetStringProperty("m_sequenceName"), result);
                }
            }

            var visited = new HashSet<long>();
            foreach (var additiveRoot in additiveRoots)
            {
                CollectSequenceNames(additiveRoot, nodesById, visited, result);
            }
        }

        // Walks down from an additive slot, gathering the sequence names of every sequence node it reaches.
        private static void CollectSequenceNames(long nodeId, Dictionary<long, KVObject> nodesById, HashSet<long> visited, HashSet<string> result)
        {
            if (nodeId == InvalidNodeId || !visited.Add(nodeId) || !nodesById.TryGetValue(nodeId, out var node))
            {
                return;
            }

            if (node.GetStringProperty("_class") == "CSequenceAnimNode")
            {
                AddSequence(node.GetStringProperty("m_sequenceName"), result);
            }

            // Composite nodes (blends, state machines) hold their children in arrays whose id keys vary
            // (m_nodeID, m_childNodeID, ...), so descend the whole node tree and follow every child link.
            var childIds = new List<long>();
            CollectChildLinks(node, childIds);

            foreach (var childId in childIds)
            {
                CollectSequenceNames(childId, nodesById, visited, result);
            }
        }

        // Collects the id of every child-node link nested anywhere under a node. A link is a collection
        // carrying an m_id under a key naming a child or node (m_childID, m_baseChildID, m_additiveChildID,
        // m_nodeID, m_childNodeID); param/state/tag references (m_paramID, m_stateID, m_destState) are skipped.
        private static void CollectChildLinks(KVObject obj, List<long> childIds)
        {
            foreach (var (key, value) in obj.Children)
            {
                if (value is null)
                {
                    continue;
                }

                if (IsChildLinkKey(key) && value.ValueType == KVValueType.Collection && value.ContainsKey("m_id"))
                {
                    childIds.Add(value.GetIntegerProperty("m_id"));
                    continue;
                }

                // Descend arrays and sub-objects alike: iterating a KVObject's Children yields array elements
                // as well as object properties, so nested composite nodes are reached either way.
                if (value.IsArray || value.ValueType == KVValueType.Collection)
                {
                    CollectChildLinks(value, childIds);
                }
            }
        }

        private static bool IsChildLinkKey(string? key)
            => key != null
            && (key.Contains("Child", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Node", StringComparison.OrdinalIgnoreCase));
    }
}
