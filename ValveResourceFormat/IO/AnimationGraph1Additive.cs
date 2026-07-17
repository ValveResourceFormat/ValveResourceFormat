using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Resolves which sequences a model's AG1 (Animgraph1) graph applies additively. AG1 sequences carry
    /// no additive flag of their own - additive is a per-slot property of the graph, so this walks the graph
    /// nodes to collect the sequences fed into additive slots (add nodes and additive aim matrices).
    /// </summary>
    /// <remarks>
    /// Only the uncompiled <c>CAnimationGraph</c> layout is understood (root-level <c>m_nodes</c>, <c>*AnimNode</c>
    /// classes, string <c>m_sequenceName</c>), which is what Half-Life Alyx and SteamVR Home ship inside their
    /// <c>.vanmgrph_c</c>. Modern compiled graphs (<c>CAnimGraphModelBinding</c> with <c>m_pSharedData.m_nodes</c>,
    /// <c>*UpdateNode</c> classes and integer <c>m_hSequence</c> handles, e.g. Deadlock) are not handled and
    /// resolve to an empty set. <see cref="AnimationGraphExtract"/> covers both layouts if support is needed.
    /// </remarks>
    public static class AnimationGraph1Additive
    {
        // A child id of uint.MaxValue is the graph's "no node" sentinel.
        private const long InvalidNodeId = 0xFFFFFFFF;

        /// <summary>
        /// Returns the set of sequence names the model's animation graph applies additively, or an empty
        /// set when the model has no AG1 graph.
        /// </summary>
        public static HashSet<string> GetAdditiveSequences(Model model, IFileLoader fileLoader)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var graphName = model.KeyValues?.GetStringProperty("anim_graph_resource");
            if (string.IsNullOrEmpty(graphName))
            {
                return result;
            }

            using var resource = fileLoader.LoadFileCompiled(graphName);
            if (resource?.DataBlock is not BinaryKV3 graph)
            {
                return result;
            }

            var root = graph.Data.Root;
            if (!root.ContainsKey("m_nodes"))
            {
                return result;
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

            return result;
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

        // Sequence names are collected without the '@' autoplay prefix marker.
        private static void AddSequence(string? sequence, HashSet<string> result)
        {
            if (string.IsNullOrEmpty(sequence))
            {
                return;
            }

            result.Add(sequence.StartsWith('@') ? sequence[1..] : sequence);
        }
    }
}
