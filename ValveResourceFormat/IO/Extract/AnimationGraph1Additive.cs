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
    /// Both graph layouts are understood: the uncompiled <c>CAnimationGraph</c> layout (root-level
    /// <c>m_nodes</c> id/value pairs, <c>*AnimNode</c> classes, string <c>m_sequenceName</c>), which is what
    /// Half-Life Alyx and SteamVR Home ship inside their <c>.vanmgrph_c</c>, and the modern compiled layout
    /// (<c>m_pSharedData.m_nodes</c> array, <c>*UpdateNode</c> classes, integer <c>m_hSequence</c> handles
    /// resolved against the model's sequence table), which is what Deadlock ships.
    /// </remarks>
    public static partial class AnimationGraph1Additive
    {
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

            if (root.GetStringProperty("_class") == "CAnimationGraph")
            {
                CollectFromUncompiledGraph(root, result);
            }
            else
            {
                CollectFromCompiledGraph(root, model, fileLoader, result);
            }

            return result;
        }

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
