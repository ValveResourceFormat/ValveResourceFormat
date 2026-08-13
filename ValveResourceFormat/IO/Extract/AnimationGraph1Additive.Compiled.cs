using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// The walker for the compiled graph layout: nodes sit in one flat array, children reference
    /// each other by array index (<c>m_nodeIndex</c> collections) and sequences by integer
    /// <c>m_hSequence</c> handles into the model's sequence table (local ASEQ names followed by
    /// referenced animations).
    /// </summary>
    public static partial class AnimationGraph1Additive
    {
        private static void CollectFromCompiledGraph(KVObject root, Model model, IFileLoader fileLoader, HashSet<string> result)
        {
            var container = root.ContainsKey("m_pSharedData") ? root.GetSubCollection("m_pSharedData") : root;
            if (container == null || !container.ContainsKey("m_nodes"))
            {
                return;
            }

            var nodes = container.GetArray("m_nodes");
            var modelInfo = new AnimGraphModelInfo(fileLoader, () => model.Resource);

            var additiveRoots = new List<int>();
            foreach (var node in nodes)
            {
                var nodeClass = node.GetStringProperty("_class");

                if (nodeClass == "CAddUpdateNode")
                {
                    // m_pChild1 is the base input, m_pChild2 the additive input.
                    var additiveChild = node.GetSubCollection("m_pChild2");
                    if (additiveChild != null && additiveChild.ContainsKey("m_nodeIndex"))
                    {
                        additiveRoots.Add(additiveChild.GetInt32Property("m_nodeIndex"));
                    }
                }
                else if (nodeClass == "CAimMatrixUpdateNode"
                    && (node.GetSubCollection("m_opFixedSettings")?.GetStringProperty("m_eBlendMode", "")
                        .Contains("Additive", StringComparison.Ordinal) ?? false))
                {
                    AddCompiledSequence(node, modelInfo, result);
                }
            }

            var visited = new HashSet<int>();
            foreach (var additiveRoot in additiveRoots)
            {
                CollectCompiledSequenceNames(additiveRoot, nodes, visited, modelInfo, result);
            }
        }

        // Walks down from an additive slot in a compiled graph, gathering the sequences of every node
        // it reaches. Nodes under an additive input hold delta content, so every m_hSequence in the
        // subtree is collected regardless of the node class carrying it. Subtract nodes are the
        // exception: they derive the delta at runtime from absolute inputs (action minus reference
        // pose), so the sequences below them are absolute content and are not collected.
        private static void CollectCompiledSequenceNames(int nodeIndex, IReadOnlyList<KVObject> nodes, HashSet<int> visited, AnimGraphModelInfo modelInfo, HashSet<string> result)
        {
            if (nodeIndex < 0 || nodeIndex >= nodes.Count || !visited.Add(nodeIndex))
            {
                return;
            }

            var node = nodes[nodeIndex];
            if (node.GetStringProperty("_class") == "CSubtractUpdateNode")
            {
                return;
            }

            AddCompiledSequence(node, modelInfo, result);

            var childIndices = new List<int>();
            CollectCompiledChildLinks(node, childIndices);

            foreach (var childIndex in childIndices)
            {
                CollectCompiledSequenceNames(childIndex, nodes, visited, modelInfo, result);
            }
        }

        // Child links in compiled nodes are collections holding m_nodeIndex, nested under varying keys
        // (m_pChildNode, m_pChild1, m_children[], Blend2D m_items[].m_pChild, state data), so descend
        // the whole node tree and follow every one.
        private static void CollectCompiledChildLinks(KVObject obj, List<int> childIndices)
        {
            foreach (var (_, value) in obj.Children)
            {
                if (value is null)
                {
                    continue;
                }

                if (value.ValueType == KVValueType.Collection && value.ContainsKey("m_nodeIndex"))
                {
                    childIndices.Add(value.GetInt32Property("m_nodeIndex"));
                    continue;
                }

                if (value.IsArray || value.ValueType == KVValueType.Collection)
                {
                    CollectCompiledChildLinks(value, childIndices);
                }
            }
        }

        private static void AddCompiledSequence(KVObject node, AnimGraphModelInfo modelInfo, HashSet<string> result)
        {
            if (!node.ContainsKey("m_hSequence"))
            {
                return;
            }

            var sequenceIndex = node.GetIntegerProperty("m_hSequence");
            if (sequenceIndex < 0)
            {
                return;
            }

            AddSequence(modelInfo.GetSequenceName(sequenceIndex), result);
        }
    }
}
