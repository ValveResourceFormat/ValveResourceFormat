using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO
{
    /// <summary>
    /// Resolves the animation clips referenced by a model's animation graphs (animgraph2).
    /// </summary>
    public static class AnimationGraphLoader
    {
        private static readonly string NmClipExtension = ResourceType.NmClip.GetExtension()!;
        private static readonly string NmGraphExtension = ResourceType.NmGraph.GetExtension()!;

        /// <summary>
        /// Gets the clip (.vnmclip) resource names referenced by the model's animation graphs, recursing
        /// into nested graphs. These are not part of <see cref="Model.GetAllAnimations"/>. The returned
        /// list is de-duplicated (each clip appears once) and preserves first-seen order.
        /// </summary>
        public static IReadOnlyList<string> GetClipNames(Model model, IFileLoader fileLoader)
        {
            var graphRefs = model.Data.GetArray("m_animGraph2Refs");
            if (graphRefs == null)
            {
                return [];
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var clipNames = new List<string>();
            foreach (var graphRef in graphRefs)
            {
                CollectClips(graphRef.GetStringProperty("m_hGraph"), fileLoader, visited, clipNames);
            }

            return clipNames;
        }

        private static void CollectClips(string graphName, IFileLoader fileLoader, HashSet<string> visited, List<string> clipNames)
        {
            if (!visited.Add(graphName) || fileLoader.LoadFileCompiled(graphName)?.DataBlock is not BinaryKV3 graph)
            {
                return;
            }

            var resources = graph.Data.Root.GetArray<string>("m_resources");
            if (resources == null)
            {
                return;
            }

            foreach (var resource in resources)
            {
                if (resource.EndsWith(NmClipExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (visited.Add(resource))
                    {
                        clipNames.Add(resource);
                    }
                }
                else if (resource.EndsWith(NmGraphExtension, StringComparison.OrdinalIgnoreCase))
                {
                    CollectClips(resource, fileLoader, visited, clipNames);
                }
            }
        }
    }
}
