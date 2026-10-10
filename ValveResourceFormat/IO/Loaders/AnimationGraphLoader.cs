using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
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
        /// into nested graphs. <see cref="Model.GetAllAnimations(IFileLoader)"/> loads these clips as part of the
        /// model's animation set. The returned list is de-duplicated (each clip appears once) and
        /// preserves first-seen order.
        /// </summary>
        public static IReadOnlyList<string> GetClipNames(Model model, IFileLoader fileLoader)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var clipNames = new List<string>();

            foreach (var (_, graphPath) in model.AnimGraph2References)
            {
                CollectClips(graphPath, fileLoader, visited, clipNames);
            }

            return clipNames;
        }

        /// <summary>
        /// Loads clips in parallel, each distinct name once. A clip that is missing is left out.
        /// </summary>
        /// <param name="clipNames">Clip (.vnmclip) resource names.</param>
        /// <param name="fileLoader">Loader for the clip files, which must allow loading from several threads at once.</param>
        /// <returns>The loaded clips by resource name, compared without case.</returns>
        public static Dictionary<string, ClipAnimation> LoadClips(IEnumerable<string> clipNames, IFileLoader fileLoader)
        {
            var names = clipNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var clips = new ClipAnimation?[names.Length];

            Parallel.For(0, names.Length, i =>
            {
                if (fileLoader.LoadFileCompiled(names[i])?.DataBlock is AnimationClip clip)
                {
                    clips[i] = new ClipAnimation(clip);
                }
            });

            var result = new Dictionary<string, ClipAnimation>(names.Length, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < names.Length; i++)
            {
                if (clips[i] is { } clip)
                {
                    result[names[i]] = clip;
                }
            }

            return result;
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
