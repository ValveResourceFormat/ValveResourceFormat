using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    public partial class AnimationGraph
    {
        /// <summary>
        /// The files a graph and every graph it references need, read before any of them is built. A graph
        /// tree can reference a thousand clips, which load here in parallel and once per skeleton.
        /// </summary>
        private sealed class GraphResources
        {
            /// <summary>Gets the referenced files other than clips, by resource name.</summary>
            public Dictionary<string, Resource?> Files { get; } = [];

            /// <summary>Gets the skeletons the graphs animate, by resource name.</summary>
            public Dictionary<string, (Skeleton Skeleton, AnimLib.Skeleton AnimLibSkeleton)> Skeletons { get; } = [];

            /// <summary>Gets the clips, by resource name and the skeleton of the graph playing them.</summary>
            public ConcurrentDictionary<(string Clip, string Skeleton), GraphClip> Clips { get; } = [];

            /// <summary>Gets the first skeleton a graph of the tree references but the loader could not provide.</summary>
            public string? MissingSkeleton { get; private set; }

            public static string GetSkeletonName(KVObject graph) => graph.GetProperty<string>("m_skeleton") ?? string.Empty;

            /// <summary>Reads everything the graph tree needs, stopping at the first missing skeleton.</summary>
            /// <param name="graphDefinition">The graph at the root of the tree.</param>
            /// <param name="fileLoader">Loader for the files.</param>
            /// <param name="loadedClips">Clips already loaded elsewhere, by resource name, used instead of reading them again.</param>
            public static GraphResources Load(BinaryKV3 graphDefinition, IFileLoader fileLoader, Func<string, ClipAnimation?>? loadedClips = null)
            {
                var resources = new GraphResources();
                HashSet<(string Clip, string Skeleton)> clipKeys = [];

                if (!resources.Walk(graphDefinition, fileLoader, clipKeys, []))
                {
                    return resources;
                }

                var readClips = AnimationGraphLoader.LoadClips(
                    clipKeys.Select(static key => key.Clip).Where(clip => loadedClips?.Invoke(clip) == null),
                    fileLoader);

                Parallel.ForEach(clipKeys, key =>
                {
                    if ((loadedClips?.Invoke(key.Clip) ?? readClips.GetValueOrDefault(key.Clip)) is { } animation)
                    {
                        resources.Clips[key] = new GraphClip(animation, resources.Skeletons[key.Skeleton].Skeleton);
                    }
                });

                return resources;
            }

            private bool Walk(BinaryKV3 graphDefinition, IFileLoader fileLoader, HashSet<(string Clip, string Skeleton)> clipKeys, HashSet<BinaryKV3> visited)
            {
                if (!visited.Add(graphDefinition))
                {
                    return true;
                }

                var graph = graphDefinition.Data.Root;
                var skeletonName = GetSkeletonName(graph);

                if (!Skeletons.TryGetValue(skeletonName, out _))
                {
                    if (skeletonName.Length == 0 || fileLoader.LoadFileCompiled(skeletonName)?.DataBlock is not BinaryKV3 skeletonFile)
                    {
                        MissingSkeleton = skeletonName;
                        return false;
                    }

                    var skeletonData = skeletonFile.Data.Root;
                    Skeletons[skeletonName] = (Skeleton.FromSkeletonData(skeletonData), new AnimLib.Skeleton(skeletonData));
                }

                foreach (var resourceName in graph.GetArray<string>("m_resources") ?? [])
                {
                    // Clips are left for the parallel load, everything else is needed to find more of them
                    if (resourceName.EndsWith(".vnmclip", StringComparison.OrdinalIgnoreCase))
                    {
                        clipKeys.Add((resourceName, skeletonName));
                        continue;
                    }

                    if (!Files.TryGetValue(resourceName, out var file))
                    {
                        file = fileLoader.LoadFileCompiled(resourceName);
                        Files[resourceName] = file;
                    }

                    switch (file?.DataBlock)
                    {
                        case BinaryKV3 childDefinition when file.ResourceType == ResourceType.NmGraph:
                            if (!Walk(childDefinition, fileLoader, clipKeys, visited))
                            {
                                return false;
                            }

                            break;
                        case AnimationClip clip when file.ResourceType == ResourceType.NmClip:
                            Clips[(resourceName, skeletonName)] = new GraphClip(new ClipAnimation(clip), Skeletons[skeletonName].Skeleton);
                            break;
                    }
                }

                return true;
            }
        }
    }
}
